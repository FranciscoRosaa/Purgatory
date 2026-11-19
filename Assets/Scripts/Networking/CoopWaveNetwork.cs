using System.Collections.Generic;
using Unity.Networking.Transport;
using Unity.Collections;
using UnityEngine;

internal sealed partial class CoopSession
{
    private const byte WaveEnemyMessage = 3;
    private const int WaveHeaderBytes = 25;
    private WaveDirector waveDirector;
    private readonly Dictionary<ushort, uint> lastEnemySnapshots = new Dictionary<ushort, uint>();

    private sealed class PendingLungeHit
    {
        internal LungeMan Enemy;
        internal LungeMan.AttackLane Lane;
        internal float Contact;
        internal bool Hit, Dodged;
    }

    private int CombatBytes => waveDirector != null
        ? 13 + LibraryHazardBytes + WaveHeaderBytes + waveDirector.Spawned.Count * 2
        : CombatSnapshotBytes + 1 + LibraryHazardBytes;

    private void WriteWaveSnapshot(ref DataStreamWriter writer)
    {
        writer.WriteByte((byte)waveDirector.Stage);
        writer.WriteUShort((ushort)waveDirector.CurrentWave);
        writer.WriteUShort((ushort)waveDirector.TotalWaves);
        writer.WriteUShort((ushort)waveDirector.CurrentEnemyLimit);
        writer.WriteInt(waveDirector.AliveEnemies);
        writer.WriteInt(waveDirector.EnemiesWaitingToSpawn);
        writer.WriteInt(waveDirector.TotalEnemiesInCurrentWave);
        writer.WriteFloat(waveDirector.Countdown);
        writer.WriteUShort((ushort)waveDirector.Spawned.Count);
        foreach (var enemy in waveDirector.Spawned) writer.WriteUShort(enemy.Id);
    }

    private void ReadWaveSnapshot(ref DataStreamReader reader)
    {
        if (reader.Length - reader.GetBytesRead() < WaveHeaderBytes) return;
        var stage = (WaveDirector.WaveStage)reader.ReadByte();
        int current = reader.ReadUShort(), total = reader.ReadUShort();
        int limit = reader.ReadUShort();
        int alive = reader.ReadInt(), pending = reader.ReadInt(), totalEnemies = reader.ReadInt();
        float countdown = reader.ReadFloat();
        int count = reader.ReadUShort();
        if (stage > WaveDirector.WaveStage.Complete || current > total ||
            limit < 1 || limit > WaveDirector.MaximumConcurrentEnemies ||
            alive < 0 || alive > WaveDirector.MaximumConcurrentEnemies || pending < 0 || totalEnemies < 0 ||
            count > 128 || reader.Length - reader.GetBytesRead() != count * 2) return;
        var manifest = new ushort[count];
        for (int i = 0; i < count; i++) manifest[i] = reader.ReadUShort();
        waveDirector?.ApplyRemoteWave(stage, current, total, limit, alive, pending, totalEnemies, countdown, manifest);
        foreach (ushort id in new List<ushort>(lastEnemySnapshots.Keys))
            if (waveDirector == null || !waveDirector.AcceptsReplica(id)) lastEnemySnapshots.Remove(id);
    }

    private static int EnemyStateBytes(WaveEnemyKind kind) => kind switch
    {
        WaveEnemyKind.LungeMan => 53,
        WaveEnemyKind.Giant => 53,
        WaveEnemyKind.RedNinja => 63,
        WaveEnemyKind.TrueHealer => 42,
        WaveEnemyKind.GiantKing => GiantKingStateBytes,
        WaveEnemyKind.BlackNinja => 74,
        WaveEnemyKind.ArcaneEye => 70,
        WaveEnemyKind.Wizard => 118,
        _ => -1
    };

    private void BroadcastWaveEnemies(uint snapshotSequence)
    {
        if (waveDirector == null) return;
        foreach (var entry in waveDirector.Spawned)
        {
            if (entry.Enemy == null) continue;
            int bytes = EnemyStateBytes(entry.Kind);
            if (bytes < 0) continue;
            foreach (var peer in peers)
            {
                if (driver.BeginSend(peer.Key, out var writer, 8 + bytes) != 0) continue;
                writer.WriteByte(WaveEnemyMessage);
                writer.WriteUInt(snapshotSequence);
                writer.WriteUShort(entry.Id);
                writer.WriteByte((byte)entry.Kind);
                switch (entry.Enemy)
                {
                    case LungeMan lunge: WriteEnemy(ref writer, lunge.GetState()); break;
                    case GiantChargeAttack giantEnemy: WriteGiant(ref writer, giantEnemy.GetState()); break;
                    case RedNinja red: WriteNinja(ref writer, red.GetState()); break;
                    case TrueHealer healerEnemy: WriteHealer(ref writer, healerEnemy.GetState()); break;
                    case GiantKing boss: WriteKing(ref writer, boss.GetState()); break;
                    case BlackNinja black: WriteBlackNinja(ref writer, black.GetState()); break;
                    case ArcaneEye eye: WriteArcaneEye(ref writer, eye.GetState()); break;
                    case Wizard wizard: WriteWizard(ref writer, wizard.GetState()); break;
                }
                driver.EndSend(writer);
            }
        }
    }

    private void ReceiveWaveEnemy(DataStreamReader reader)
    {
        if (waveDirector == null || reader.Length < 8 || reader.ReadByte() != WaveEnemyMessage) return;
        uint sequence = reader.ReadUInt();
        ushort id = reader.ReadUShort();
        var kind = (WaveEnemyKind)reader.ReadByte();
        if (id == 0 || EnemyStateBytes(kind) < 0 || reader.Length != 8 + EnemyStateBytes(kind) ||
            !waveDirector.AcceptsReplica(id) ||
            (lastEnemySnapshots.TryGetValue(id, out uint previous) && sequence <= previous)) return;
        // Each actor has its own sequence and identity: several enemies of the same type are supported.
        switch (kind)
        {
            case WaveEnemyKind.Wizard:
                var wizard = ReadWizard(ref reader);
                if (!ValidEnemyPosition(wizard.Position) || !ValidEnemyPosition(wizard.Destination) ||
                    wizard.Phase > Wizard.Phase.Dead || wizard.Health < 0 || wizard.Health > Wizard.MaximumHealth ||
                    !float.IsFinite(wizard.Yaw) || !float.IsFinite(wizard.Elapsed) || wizard.Elapsed < 0f ||
                    !ValidWizardOrb(wizard.First) || !ValidWizardOrb(wizard.Second)) return;
                (waveDirector.GetOrSpawnReplica(id, kind, wizard.Position) as Wizard)?.ApplyState(wizard);
                break;
            case WaveEnemyKind.ArcaneEye:
                var eye = ReadArcaneEye(ref reader);
                if (!ValidEnemyPosition(eye.Position) || !ValidEnemyPosition(eye.Origin) ||
                    !ValidEnemyPosition(eye.Direction) || eye.Phase > ArcaneEye.Phase.Dead ||
                    eye.Attack > ArcaneEye.Attack.Chain || eye.Health < 0 || eye.Health > ArcaneEye.MaximumHealth ||
                    !float.IsFinite(eye.Yaw) || !float.IsFinite(eye.Elapsed) || eye.Elapsed < 0f) return;
                (waveDirector.GetOrSpawnReplica(id, kind, eye.Position) as ArcaneEye)?.ApplyState(eye);
                break;
            case WaveEnemyKind.BlackNinja:
                var black=ReadBlackNinja(ref reader);
                if(!ValidEnemyPosition(black.Position) || !ValidEnemyPosition(black.Origin) || !ValidEnemyPosition(black.Direction) ||
                    black.Phase>BlackNinja.Phase.Dead || black.Strike>2 || black.Health<0 || black.Health>100000 ||
                    !float.IsFinite(black.Yaw) || !float.IsFinite(black.Elapsed) || !float.IsFinite(black.Length) || black.Length<0 ||
                    !float.IsFinite(black.Opacity) || black.Opacity<0 || black.Opacity>1)return;
                (waveDirector.GetOrSpawnReplica(id,kind,black.Position) as BlackNinja)?.ApplyState(black);
                break;
            case WaveEnemyKind.LungeMan:
                var lunge = ReadEnemy(ref reader);
                if (!ValidEnemyPosition(lunge.Position)) return;
                (waveDirector.GetOrSpawnReplica(id, kind, lunge.Position) as LungeMan)?.ApplyState(lunge);
                break;
            case WaveEnemyKind.Giant:
                var giantState = ReadGiant(ref reader);
                if (!ValidEnemyPosition(giantState.Position)) return;
                (waveDirector.GetOrSpawnReplica(id, kind, giantState.Position) as GiantChargeAttack)?.ApplyState(giantState);
                break;
            case WaveEnemyKind.RedNinja:
                var red = ReadNinja(ref reader);
                if (!ValidEnemyPosition(red.Position)) return;
                (waveDirector.GetOrSpawnReplica(id, kind, red.Position) as RedNinja)?.ApplyState(red);
                break;
            case WaveEnemyKind.TrueHealer:
                var healerState = ReadHealer(ref reader);
                if (!ValidEnemyPosition(healerState.Position)) return;
                (waveDirector.GetOrSpawnReplica(id, kind, healerState.Position) as TrueHealer)?.ApplyState(healerState);
                break;
            case WaveEnemyKind.GiantKing:
                var kingState = ReadKing(ref reader);
                if (!ValidEnemyPosition(kingState.Position) || !ValidEnemyPosition(kingState.Target) ||
                    !ValidEnemyPosition(kingState.Origin) || kingState.Phase > GiantKing.Phase.Dead ||
                    kingState.Health < 0 || !float.IsFinite(kingState.Elapsed) || !float.IsFinite(kingState.Yaw) ||
                    !float.IsFinite(kingState.HazardElapsed)) return;
                (waveDirector.GetOrSpawnReplica(id, kind, kingState.Position) as GiantKing)?.ApplyState(kingState);
                break;
        }
        lastEnemySnapshots[id] = sequence;
    }

    private static bool ValidEnemyPosition(Vector3 value) => Valid(new Vector2(value.x, value.z)) && Mathf.Abs(value.y) < 10000f;
}
