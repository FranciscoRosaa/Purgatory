using System.Collections.Generic;
using Unity.Networking.Transport;
using Unity.Collections;
using UnityEngine;

internal sealed partial class CoopSession
{
    private const byte WaveEnemyMessage = 3;
    private const int WaveHeaderBytes = 27;
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
        ? 13 + WaveHeaderBytes + waveDirector.Spawned.Count * 2 : CombatSnapshotBytes + 1;

    private void WriteWaveSnapshot(ref DataStreamWriter writer)
    {
        writer.WriteByte((byte)waveDirector.Stage);
        writer.WriteUShort((ushort)waveDirector.CurrentWave);
        writer.WriteUShort((ushort)waveDirector.TotalWaves);
        writer.WriteUShort((ushort)waveDirector.CurrentSubWave);
        writer.WriteUShort((ushort)waveDirector.TotalSubWaves);
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
        int currentSub = reader.ReadUShort(), totalSub = reader.ReadUShort();
        int alive = reader.ReadInt(), pending = reader.ReadInt(), totalEnemies = reader.ReadInt();
        float countdown = reader.ReadFloat();
        int count = reader.ReadUShort();
        if (stage > WaveDirector.WaveStage.SubWaveCountdown || current > total || currentSub > totalSub ||
            alive < 0 || alive > WaveDirector.MaximumConcurrentEnemies || pending < 0 || totalEnemies < 0 ||
            count > 128 || reader.Length - reader.GetBytesRead() != count * 2) return;
        var manifest = new ushort[count];
        for (int i = 0; i < count; i++) manifest[i] = reader.ReadUShort();
        waveDirector?.ApplyRemoteWave(stage, current, total, currentSub, totalSub, alive, pending, totalEnemies, countdown, manifest);
        foreach (ushort id in new List<ushort>(lastEnemySnapshots.Keys))
            if (waveDirector == null || !waveDirector.AcceptsReplica(id)) lastEnemySnapshots.Remove(id);
    }

    private static int EnemyStateBytes(WaveEnemyKind kind) => kind switch
    {
        WaveEnemyKind.LungeMan => 53,
        WaveEnemyKind.Giant => 53,
        WaveEnemyKind.RedNinja => 63,
        WaveEnemyKind.TrueHealer => 42,
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
        }
        lastEnemySnapshots[id] = sequence;
    }

    private static bool ValidEnemyPosition(Vector3 value) => Valid(new Vector2(value.x, value.z)) && Mathf.Abs(value.y) < 10000f;
}
