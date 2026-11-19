using Unity.Collections;
using UnityEngine;

internal sealed partial class CoopSession
{
    internal void ResolvePeersInArcaneAttack(ArcaneEye enemy, float age, float dt)
    {
        if (!InMatch || !IsHost) return;
        foreach (var peer in peers.Values)
        {
            if (peer.Health <= 0) continue;
            var feet = ToWorld(peer.Position);
            feet.y = localBlock != null ? localBlock.position.y : enemy.transform.position.y;
            if (localMovement != null && localMovement.TryGetGroundHeight(feet, out float ground)) feet.y = ground;
            float dashAge = Time.time - peer.DashStartedAt;
            bool dodging = localMovement != null && dashAge >= 0f && dashAge <= localMovement.DashDuration;
            int damage = enemy.ResolveContact(peer.Id, feet, dodging, age, dt, out bool dodge);
            if (dodge) peer.DodgeSequence++;
            peer.Health = Mathf.Max(0, peer.Health - damage);
        }
    }

    private static void WriteArcaneEye(ref DataStreamWriter writer, ArcaneEye.State state)
    {
        writer.WriteByte((byte)state.Phase);
        WriteVector(ref writer, state.Position);
        writer.WriteFloat(state.Yaw);
        writer.WriteFloat(state.Elapsed);
        writer.WriteInt(state.Health);
        writer.WriteUInt(state.HitSequence);
        writer.WriteULong(state.HitHistory);
        WriteVector(ref writer, state.Origin);
        WriteVector(ref writer, state.Direction);
        writer.WriteInt(state.Seed);
        writer.WriteByte((byte)state.Attack);
        writer.WriteUInt(state.AttackSequence);
    }

    private static ArcaneEye.State ReadArcaneEye(ref DataStreamReader reader) => new ArcaneEye.State
    {
        Phase = (ArcaneEye.Phase)reader.ReadByte(), Position = ReadVector(ref reader),
        Yaw = reader.ReadFloat(), Elapsed = reader.ReadFloat(), Health = reader.ReadInt(),
        HitSequence = reader.ReadUInt(), HitHistory = reader.ReadULong(),
        Origin = ReadVector(ref reader), Direction = ReadVector(ref reader),
        Seed = reader.ReadInt(), Attack = (ArcaneEye.Attack)reader.ReadByte(), AttackSequence = reader.ReadUInt()
    };
}
