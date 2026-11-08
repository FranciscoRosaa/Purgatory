using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class CoopSession : MonoBehaviour
{
    internal const string GameScene = "Assets/Scenes/SampleScene.unity";
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const int MaxPlayers = 4;
    private const float ConnectionTimeout = 20f;
    private const string Protocol = "dtls";
    private const byte PositionMessage = 1;
    private const byte SnapshotMessage = 2;
    private const int SnapshotHeaderBytes = 8;
    private const int PlayerBytes = 10;
    private const int PositionBytes = 14;

    private sealed class Peer
    {
        public byte Id;
        public Vector2 Position;
        public uint LastSequence;
        public byte Color;
    }

    private sealed class Avatar
    {
        public GameObject Object;
        public byte Color;
    }

    private readonly Dictionary<NetworkConnection, Peer> peers = new Dictionary<NetworkConnection, Peer>();
    private readonly Dictionary<byte, Avatar> avatars = new Dictionary<byte, Avatar>();
    private NetworkDriver driver;
    private NetworkConnection server;
    private Transform localBlock;
    private Task authentication;
    private string pendingInvite;
    private bool hosting, busy, started, previousRunInBackground;
    private int operation;
    private float nextTick, connectionDeadline;
    private uint sequence, lastSnapshot;
    private byte localId, localColor;

    internal bool IsHost => driver.IsCreated && hosting;
    internal bool Active => busy || driver.IsCreated;
    internal bool CanStart => IsHost && !started && peers.Count > 0;
    internal int Count { get; private set; }
    internal string Status { get; private set; } = "Host a game or enter an invite code.";
    internal string Invite { get; private set; }

    private async Task Authenticate()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private Task EnsureAuthenticated()
    {
        if (authentication == null || authentication.IsCompleted)
            authentication = Authenticate();
        return authentication;
    }

    internal void Host()
    {
        if (!Active)
            Connect(null);
    }

    internal void Join(string input)
    {
        if (Active)
            return;

        string code = input.Trim().ToUpperInvariant();
        if (!IsInviteCode(code))
        {
            Status = "Paste a valid Relay invite code.";
            return;
        }
        Connect(code);
    }

    internal static bool IsInviteCode(string code) => code.Length > 0 && code.Length <= 12 &&
        System.Linq.Enumerable.All(code, c => c >= 'A' && c <= 'Z' || c >= '0' && c <= '9');

    private async void Connect(string code)
    {
        int currentOperation = ++operation;
        busy = true;
        localColor = (byte)CharacterProfiles.Active.Color;
        previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        Status = code == null ? "Creating Relay lobby..." : "Connecting through Relay...";
        try
        {
            await EnsureAuthenticated();
            if (currentOperation != operation)
                return;

            if (code == null)
            {
                var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                if (currentOperation != operation)
                    return;
                string invite = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                if (currentOperation != operation)
                    return;

                Open(AllocationUtils.ToRelayServerData(allocation, Protocol));
                if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0 || driver.Listen() != 0)
                    throw new InvalidOperationException("Could not bind the Relay host.");
                hosting = true;
                localId = 1;
                Count = 1;
                pendingInvite = invite;
            }
            else
            {
                var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                if (currentOperation != operation)
                    return;

                var relay = AllocationUtils.ToRelayServerData(allocation, Protocol);
                Open(relay);
                server = driver.Connect(relay.Endpoint);
                if (!server.IsCreated)
                    throw new InvalidOperationException("Could not connect to the Relay host.");
            }
        }
        catch (Exception error)
        {
            if (currentOperation == operation)
            {
                Debug.LogWarning($"Co-op Relay setup failed: {error.GetType().Name}: {error.Message}");
                Stop(code == null ? "Could not create a Relay lobby. Check Unity Services and retry."
                    : "Could not join. Check the code and ask the host to keep the lobby open.");
            }
        }
        finally
        {
            if (currentOperation == operation)
                busy = false;
        }
    }

    private void Open(RelayServerData relay)
    {
        var settings = new NetworkSettings();
        try
        {
            settings.WithRelayParameters(ref relay);
            driver = NetworkDriver.Create(settings);
        }
        finally
        {
            settings.Dispose();
        }
        nextTick = 0f;
        connectionDeadline = Time.unscaledTime + ConnectionTimeout;
    }

    internal void StartGame()
    {
        if (CanStart)
            EnterGame();
    }

    private void EnterGame()
    {
        started = true;
        SceneManager.sceneLoaded += OnGameLoaded;
        SceneManager.LoadScene(GameScene);
    }

    private void OnGameLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != GameScene)
            return;
        SceneManager.sceneLoaded -= OnGameLoaded;
        localBlock = GameObject.Find("Personagem").transform;
        Vector2 spawn = Spawn(localId);
        localBlock.position = new Vector3(spawn.x, localBlock.position.y, spawn.y);
    }

    private static Vector2 Spawn(byte id) => new Vector2((id - 1) * 2f, 0f);
    private Vector2 LocalPosition => localBlock == null ? Spawn(localId) :
        new Vector2(localBlock.position.x, localBlock.position.z);

    private void Update()
    {
        if (!driver.IsCreated)
            return;
#if ENABLE_INPUT_SYSTEM
        if (started && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            Stop();
            return;
        }
#endif

        driver.ScheduleUpdate().Complete();
        var relayStatus = driver.GetRelayConnectionStatus();
        if (relayStatus == RelayConnectionStatus.AllocationInvalid)
        {
            Stop("The Relay session expired.");
            return;
        }
        if (IsHost && pendingInvite != null && relayStatus == RelayConnectionStatus.Established)
        {
            Invite = pendingInvite;
            pendingInvite = null;
            Status = "Lobby open. Copy the invite code to share it.";
        }

        if (IsHost)
            AcceptPlayers();

        for (int i = 0; i < 64; i++)
        {
            var kind = driver.PopEvent(out var connection, out var reader);
            if (kind == NetworkEvent.Type.Empty)
                break;

            if (kind == NetworkEvent.Type.Disconnect)
            {
                if (IsHost)
                    RemovePeer(connection);
                else if (connection == server)
                {
                    Stop("Disconnected from the Relay host.");
                    return;
                }
            }
            else if (kind == NetworkEvent.Type.Data)
            {
                if (IsHost)
                    ReceivePosition(connection, reader);
                else if (connection == server)
                    ReceiveSnapshot(reader);
            }
        }

        float now = Time.unscaledTime;
        if (now > connectionDeadline && (IsHost ? Invite == null : localId == 0))
        {
            Stop("Relay connection timed out.");
            return;
        }
        if (now < nextTick)
            return;
        nextTick = now + 0.05f;

        if (IsHost)
            Broadcast();
        else if (localId != 0)
            SendPosition();
    }

    private void AcceptPlayers()
    {
        NetworkConnection connection;
        while ((connection = driver.Accept()).IsCreated)
        {
            if (started || peers.Count >= MaxPlayers - 1)
            {
                driver.Disconnect(connection);
                continue;
            }

            byte id = 2;
            while (System.Linq.Enumerable.Any(peers.Values, peer => peer.Id == id))
                id++;
            peers.Add(connection, new Peer { Id = id, Position = Spawn(id) });
            Count = peers.Count + 1;
        }
    }

    private void RemovePeer(NetworkConnection connection)
    {
        if (!peers.TryGetValue(connection, out var peer))
            return;
        peers.Remove(connection);
        RemoveAvatar(peer.Id);
        Count = peers.Count + 1;
    }

    private void SendPosition()
    {
        if (driver.BeginSend(server, out var writer, PositionBytes) != 0)
            return;
        writer.WriteByte(PositionMessage);
        writer.WriteUInt(++sequence);
        WritePosition(ref writer, LocalPosition);
        writer.WriteByte(localColor);
        driver.EndSend(writer);
    }

    private void ReceivePosition(NetworkConnection connection, DataStreamReader reader)
    {
        if (reader.Length != PositionBytes || reader.ReadByte() != PositionMessage ||
            !peers.TryGetValue(connection, out var peer))
            return;

        uint positionSequence = reader.ReadUInt();
        var position = ReadPosition(ref reader);
        byte color = reader.ReadByte();
        if (positionSequence <= peer.LastSequence || !Valid(position) || color >= CharacterProfiles.ColorCount)
            return;
        peer.LastSequence = positionSequence;
        peer.Color = color;
        if (started)
            peer.Position = position;
        if (localBlock != null)
            SetAvatar(peer.Id, position, color);
    }

    private void Broadcast()
    {
        uint snapshotSequence = ++sequence;
        foreach (var recipient in peers)
        {
            if (driver.BeginSend(recipient.Key, out var writer, SnapshotHeaderBytes + Count * PlayerBytes) != 0)
                continue;
            writer.WriteByte(SnapshotMessage);
            writer.WriteUInt(snapshotSequence);
            writer.WriteByte(recipient.Value.Id);
            writer.WriteByte(started ? (byte)1 : (byte)0);
            writer.WriteByte((byte)Count);
            WritePlayer(ref writer, 1, LocalPosition, localColor);
            foreach (var peer in peers.Values)
                WritePlayer(ref writer, peer.Id, peer.Position, peer.Color);
            driver.EndSend(writer);
        }
    }

    private void ReceiveSnapshot(DataStreamReader reader)
    {
        if (reader.Length < SnapshotHeaderBytes || reader.ReadByte() != SnapshotMessage)
            return;
        uint snapshotSequence = reader.ReadUInt();
        byte assignedId = reader.ReadByte();
        bool start = reader.ReadByte() == 1;
        int count = reader.ReadByte();
        if (count < 2 || count > MaxPlayers || assignedId < 2 || assignedId > MaxPlayers ||
            reader.Length != SnapshotHeaderBytes + count * PlayerBytes || snapshotSequence <= lastSnapshot)
            return;

        lastSnapshot = snapshotSequence;
        localId = assignedId;
        Count = count;
        if (!started)
        {
            Status = "Connected. Waiting for the host to start.";
            if (start)
                EnterGame();
        }
        if (localBlock == null)
            return;

        int present = 0;
        for (int i = 0; i < count; i++)
        {
            byte id = reader.ReadByte();
            var position = ReadPosition(ref reader);
            byte color = reader.ReadByte();
            if (id < 1 || id > MaxPlayers || id == localId || !Valid(position) || color >= CharacterProfiles.ColorCount)
                continue;
            present |= 1 << id;
            SetAvatar(id, position, color);
        }
        for (byte id = 1; id <= MaxPlayers; id++)
            if ((present & (1 << id)) == 0)
                RemoveAvatar(id);
    }

    private void SetAvatar(byte id, Vector2 position, byte color)
    {
        if (!avatars.TryGetValue(id, out var avatar))
        {
            var character = new GameObject("Co-op Player " + id);
            Instantiate(localBlock.Find("Character Visual"), character.transform, false);
            avatar = new Avatar { Object = character, Color = byte.MaxValue };
            avatars.Add(id, avatar);
        }
        if (avatar.Color != color)
        {
            CharacterProfiles.ApplyTint(avatar.Object, CharacterProfiles.Tint(color));
            avatar.Color = color;
        }
        avatar.Object.transform.position = new Vector3(position.x, localBlock.position.y, position.y);
    }

    private void RemoveAvatar(byte id)
    {
        if (!avatars.TryGetValue(id, out var avatar))
            return;
        Destroy(avatar.Object);
        avatars.Remove(id);
    }

    private static void WritePlayer(ref DataStreamWriter writer, byte id, Vector2 position, byte color)
    {
        writer.WriteByte(id);
        WritePosition(ref writer, position);
        writer.WriteByte(color);
    }

    private static void WritePosition(ref DataStreamWriter writer, Vector2 position)
    {
        writer.WriteFloat(position.x);
        writer.WriteFloat(position.y);
    }

    private static Vector2 ReadPosition(ref DataStreamReader reader) => new Vector2(reader.ReadFloat(), reader.ReadFloat());
    private static bool Valid(Vector2 p) => Mathf.Abs(p.x) <= 10000f && Mathf.Abs(p.y) <= 10000f;

    internal void Stop(string message = "Host a game or enter an invite code.")
    {
        bool returnToMenu = started;
        Close();
        Status = message;
        if (returnToMenu)
            SceneManager.LoadScene(MenuScene);
    }

    private void Close()
    {
        operation++;
        bool wasActive = Active;
        if (driver.IsCreated)
        {
            if (IsHost)
            {
                foreach (var connection in peers.Keys)
                    driver.Disconnect(connection);
            }
            else if (server.IsCreated)
                driver.Disconnect(server);

            driver.ScheduleUpdate().Complete();
            driver.Dispose();
            driver = default;
        }
        if (wasActive)
            Application.runInBackground = previousRunInBackground;
        server = default;
        hosting = busy = started = false;
        Count = localId = 0;
        sequence = lastSnapshot = 0;
        Invite = pendingInvite = null;
        SceneManager.sceneLoaded -= OnGameLoaded;
        peers.Clear();
        foreach (var avatar in avatars.Values)
            if (avatar.Object != null)
                Destroy(avatar.Object);
        avatars.Clear();
        localBlock = null;
    }

    private void OnDestroy() => Close();
}
