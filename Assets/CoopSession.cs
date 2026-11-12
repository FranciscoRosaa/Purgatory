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
    private const int PlayerBytes = 11;
    private const int PositionBytes = 34;
    private const int CombatSnapshotBytes = 61;
    private const float RemoteDodgeGrace = 0.15f;
    private const float MeleePacketSlack = 0.10f;
    private const byte NoDash = byte.MaxValue;

    private sealed class Peer
    {
        public byte Id;
        public Vector2 Position;
        public uint LastSequence;
        public byte Appearance;
        public byte MeleeFlash;
        public int Health = GameplayHud.StartingHealth;
        public uint LastMelee;
        public float NextMelee;
        public bool MeleeArmed;
        public Vector3 DashOrigin, DashEnd;
        public float DashStartedAt = float.NegativeInfinity;
        public LungeMan PendingLunge;
        public LungeMan.AttackLane PendingLane;
        public float LungeContact;
        public bool HitInLane, DodgedLunge;
        public uint DodgeSequence;
    }

    private sealed class Avatar
    {
        public GameObject Object;
        public byte Appearance;
        public byte MeleeFlash;
        public CharacterWhiteFlash WhiteFlash;
    }

    private readonly Dictionary<NetworkConnection, Peer> peers = new Dictionary<NetworkConnection, Peer>();
    private readonly Dictionary<byte, Avatar> avatars = new Dictionary<byte, Avatar>();
    private NetworkDriver driver;
    private NetworkConnection server;
    private Transform localBlock;
    private WasdBlockMovement localMovement;
    private PlayerMeleeCombo localMelee;
    private GameplayHud gameplayHud;
    private LungeMan lungeMan;
    private Task authentication;
    private string pendingInvite;
    private bool hosting, busy, started, previousRunInBackground;
    private int operation;
    private float nextTick, connectionDeadline;
    private uint sequence, lastSnapshot, lastDodge;
    private byte localId, localAppearance;
    private uint meleeSequence;
    private float meleeYaw;
    private byte meleeStrike;
    private byte meleeImpactFlash;

    internal bool IsHost => driver.IsCreated && hosting;
    internal bool InMatch => started && driver.IsCreated;
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
        localAppearance = (byte)CharacterProfiles.Active.Appearance;
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
        localMovement = localBlock.GetComponent<WasdBlockMovement>();
        localMelee = localBlock.GetComponent<PlayerMeleeCombo>();
        gameplayHud = FindAnyObjectByType<GameplayHud>();
        lungeMan = FindAnyObjectByType<LungeMan>();
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

        if (IsHost && started)
            ResolvePeerLunges(Time.time);
        foreach (var avatar in avatars.Values)
            avatar.WhiteFlash.Update();

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
        writer.WriteByte(localAppearance);
        writer.WriteUInt(meleeSequence);
        writer.WriteFloat(meleeYaw);
        writer.WriteByte(meleeStrike);
        writer.WriteByte(meleeImpactFlash);
        writer.WriteByte(localMovement != null ? localMovement.MeleeFlashSequence : (byte)0);
        Vector3 dashOrigin = Vector3.zero;
        byte dashAge = NoDash;
        if (localMovement != null && localMovement.TryGetDash(out dashOrigin, out float age))
            dashAge = (byte)Mathf.Clamp(Mathf.RoundToInt(age * 1000f), 0, NoDash - 1);
        writer.WriteByte(dashAge);
        WritePosition(ref writer, new Vector2(dashOrigin.x, dashOrigin.z));
        driver.EndSend(writer);
    }

    private void ReceivePosition(NetworkConnection connection, DataStreamReader reader)
    {
        if (reader.Length != PositionBytes || reader.ReadByte() != PositionMessage ||
            !peers.TryGetValue(connection, out var peer))
            return;

        uint positionSequence = reader.ReadUInt();
        var position = ReadPosition(ref reader);
        byte appearance = reader.ReadByte();
        uint melee = reader.ReadUInt();
        float yaw = reader.ReadFloat();
        byte strike = reader.ReadByte();
        byte impactFlash = reader.ReadByte();
        byte meleeFlash = reader.ReadByte();
        byte dashAge = reader.ReadByte();
        var dashOrigin = ReadPosition(ref reader);
        if (positionSequence <= peer.LastSequence || !Valid(position) || appearance >= CharacterProfiles.AppearanceCount)
            return;
        peer.LastSequence = positionSequence;
        peer.Appearance = appearance;
        if (started)
        {
            float now = Time.time;
            if (localMovement != null)
            {
                var constrained = localMovement.ClampToMovementBoundary(ToWorld(position));
                position = new Vector2(constrained.x, constrained.z);
            }
            peer.Position = position;
            RecordPeerDash(peer, dashAge, dashOrigin, position, now);
            if (meleeFlash != peer.MeleeFlash)
            {
                // Validate the attack start independently of the different clip impact times.
                peer.MeleeArmed = now + MeleePacketSlack >= peer.NextMelee;
                if (peer.MeleeArmed)
                    peer.NextMelee = now + (localMelee != null ? localMelee.AttackCooldown : PlayerMeleeCombo.DefaultAttackCooldown);
            }
            if (melee > peer.LastMelee)
            {
                peer.LastMelee = melee;
                if (peer.MeleeArmed && impactFlash == meleeFlash)
                {
                    peer.MeleeArmed = false;
                    if (peer.Health > 0 && lungeMan != null && strike < PlayerMeleeCombo.StrikeCount && Mathf.Abs(yaw) <= 360f)
                        lungeMan.ApplyMeleeStrike(ToWorld(position), Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, strike);
                }
            }
        }
        peer.MeleeFlash = meleeFlash;
        if (localBlock != null)
            SetAvatar(peer.Id, position, appearance, meleeFlash);
    }

    private void Broadcast()
    {
        uint snapshotSequence = ++sequence;
        foreach (var recipient in peers)
        {
            if (driver.BeginSend(recipient.Key, out var writer, SnapshotHeaderBytes + Count * PlayerBytes + CombatSnapshotBytes) != 0)
                continue;
            writer.WriteByte(SnapshotMessage);
            writer.WriteUInt(snapshotSequence);
            writer.WriteByte(recipient.Value.Id);
            writer.WriteByte(started ? (byte)1 : (byte)0);
            writer.WriteByte((byte)Count);
            WritePlayer(ref writer, 1, LocalPosition, localAppearance, localMovement != null ? localMovement.MeleeFlashSequence : (byte)0);
            foreach (var peer in peers.Values)
                WritePlayer(ref writer, peer.Id, peer.Position, peer.Appearance, peer.MeleeFlash);
            writer.WriteInt(recipient.Value.Health);
            writer.WriteUInt(recipient.Value.DodgeSequence);
            WriteEnemy(ref writer, lungeMan != null ? lungeMan.GetState() : default);
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
            reader.Length != SnapshotHeaderBytes + count * PlayerBytes + CombatSnapshotBytes || snapshotSequence <= lastSnapshot)
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
            byte appearance = reader.ReadByte();
            byte meleeFlash = reader.ReadByte();
            if (id < 1 || id > MaxPlayers || id == localId || !Valid(position) || appearance >= CharacterProfiles.AppearanceCount)
                continue;
            present |= 1 << id;
            SetAvatar(id, position, appearance, meleeFlash);
        }
        for (byte id = 1; id <= MaxPlayers; id++)
            if ((present & (1 << id)) == 0)
                RemoveAvatar(id);

        int health = reader.ReadInt();
        gameplayHud?.SetHealth(health);
        uint dodge = reader.ReadUInt();
        if (dodge > lastDodge)
        {
            lastDodge = dodge;
            gameplayHud?.ShowPerfectDodge(localBlock);
        }
        var enemyState = ReadEnemy(ref reader);
        lungeMan?.ApplyState(enemyState);
    }

    internal void QueueMeleeStrike(Vector3 forward, int strike)
    {
        meleeYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        meleeStrike = (byte)strike;
        meleeImpactFlash = localMovement.MeleeFlashSequence;
        meleeSequence++;
    }

    internal void FindNearestLivingPeer(Vector3 origin, ref Vector3 target, ref float bestDistance)
    {
        if (!InMatch || !IsHost)
            return;

        var originXZ = new Vector2(origin.x, origin.z);
        foreach (var peer in peers.Values)
        {
            float distance = (peer.Position - originXZ).sqrMagnitude;
            if (peer.Health <= 0 || distance >= bestDistance)
                continue;

            target = ToWorld(peer.Position);
            bestDistance = distance;
        }
    }

    private void RecordPeerDash(Peer peer, byte age, Vector2 origin, Vector2 end, float now)
    {
        if (age == NoDash || localMovement == null || !Valid(origin)
            || (end - origin).sqrMagnitude > Mathf.Pow(localMovement.DashReach + 0.25f, 2f))
            return;

        peer.DashStartedAt = now - age * 0.001f;
        peer.DashOrigin = ToWorld(origin);
        peer.DashEnd = ToWorld(end);
        if (peer.PendingLunge != null && now <= peer.LungeContact + RemoteDodgeGrace && TimedDodge(peer))
            peer.DodgedLunge = true;
    }

    internal void DamagePeersInLane(LungeMan enemy, float contact)
    {
        if (!InMatch || !IsHost)
            return;

        var lane = enemy.CurrentLane;
        foreach (var peer in peers.Values)
        {
            if (peer.Health <= 0)
                continue;

            peer.PendingLunge = enemy;
            peer.PendingLane = lane;
            peer.LungeContact = contact;
            peer.HitInLane = lane.Contains(ToWorld(peer.Position));
            peer.DodgedLunge = TimedDodge(peer);
        }
    }

    private static bool TimedDodge(Peer peer)
    {
        float ageAtContact = peer.LungeContact - peer.DashStartedAt;
        return ageAtContact >= -RemoteDodgeGrace && ageAtContact <= WasdBlockMovement.PerfectDodgeWindow
            && peer.PendingLane.Intersects(peer.DashOrigin, peer.DashEnd);
    }

    private void ResolvePeerLunges(float now)
    {
        // Wait briefly for position packets from players reacting to the same lunge.
        foreach (var peer in peers.Values)
        {
            var enemy = peer.PendingLunge;
            if (enemy == null || now < peer.LungeContact + RemoteDodgeGrace)
                continue;

            peer.PendingLunge = null;
            if (peer.DodgedLunge)
                peer.DodgeSequence++;
            else if (peer.HitInLane)
                peer.Health = Mathf.Max(0, peer.Health - enemy.RollLungeDamage());
        }
    }

    private static Vector3 ToWorld(Vector2 position) => new Vector3(position.x, 0f, position.y);

    private static void WriteEnemy(ref DataStreamWriter writer, LungeMan.State state)
    {
        writer.WriteByte((byte)state.Phase);
        WriteVector(ref writer, state.Position);
        writer.WriteFloat(state.Yaw);
        WriteVector(ref writer, state.Origin);
        writer.WriteFloat(state.Length);
        writer.WriteFloat(state.Progress);
        writer.WriteInt(state.Health);
        writer.WriteUInt(state.HitSequence);
        writer.WriteULong(state.HitHistory);
    }

    private static LungeMan.State ReadEnemy(ref DataStreamReader reader) => new LungeMan.State
    {
        Phase = (LungeMan.Phase)reader.ReadByte(),
        Position = ReadVector(ref reader),
        Yaw = reader.ReadFloat(),
        Origin = ReadVector(ref reader),
        Length = reader.ReadFloat(),
        Progress = reader.ReadFloat(),
        Health = reader.ReadInt(),
        HitSequence = reader.ReadUInt(),
        HitHistory = reader.ReadULong()
    };

    private static void WriteVector(ref DataStreamWriter writer, Vector3 point)
    {
        writer.WriteFloat(point.x);
        writer.WriteFloat(point.y);
        writer.WriteFloat(point.z);
    }

    private static Vector3 ReadVector(ref DataStreamReader reader) =>
        new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

    private void SetAvatar(byte id, Vector2 position, byte appearance, byte meleeFlash)
    {
        bool created = false;
        if (!avatars.TryGetValue(id, out var avatar))
        {
            var character = new GameObject("Co-op Player " + id);
            var visual = Instantiate(localBlock.Find("Player_Character"), character.transform, false);
            visual.localRotation = Quaternion.identity;
            avatar = new Avatar { Object = character, Appearance = byte.MaxValue, WhiteFlash = new CharacterWhiteFlash(visual) };
            avatars.Add(id, avatar);
            created = true;
        }
        if (avatar.Appearance != appearance)
        {
            avatar.WhiteFlash.Reset();
            CharacterProfiles.ApplyAppearance(avatar.Object, CharacterProfile.FromAppearance("", appearance));
            avatar.Appearance = appearance;
        }
        if (avatar.MeleeFlash != meleeFlash)
        {
            avatar.MeleeFlash = meleeFlash;
            avatar.WhiteFlash.Pulse();
        }
        var nextPosition = new Vector3(position.x, localBlock.position.y, position.y);
        var delta = nextPosition - avatar.Object.transform.position;
        bool moving = !created && delta.sqrMagnitude > 0.000001f && delta.sqrMagnitude < 25f;
        avatar.Object.transform.position = nextPosition;
        if (moving)
            avatar.Object.transform.rotation = Quaternion.LookRotation(delta);
        var animator = avatar.Object.GetComponentInChildren<Animator>();
        if (animator != null)
            animator.SetBool("IsMoving", moving);
    }

    private void RemoveAvatar(byte id)
    {
        if (!avatars.TryGetValue(id, out var avatar))
            return;
        Destroy(avatar.Object);
        avatars.Remove(id);
    }

    private static void WritePlayer(ref DataStreamWriter writer, byte id, Vector2 position, byte appearance, byte meleeFlash)
    {
        writer.WriteByte(id);
        WritePosition(ref writer, position);
        writer.WriteByte(appearance);
        writer.WriteByte(meleeFlash);
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
        sequence = lastSnapshot = lastDodge = 0;
        Invite = pendingInvite = null;
        SceneManager.sceneLoaded -= OnGameLoaded;
        peers.Clear();
        foreach (var avatar in avatars.Values)
            if (avatar.Object != null)
                Destroy(avatar.Object);
        avatars.Clear();
        localBlock = null;
        localMovement = null;
        localMelee = null;
        gameplayHud = null;
        lungeMan = null;
        meleeSequence = 0;
        meleeYaw = 0f;
        meleeStrike = 0;
        meleeImpactFlash = 0;
    }

    private void OnDestroy() => Close();
}
