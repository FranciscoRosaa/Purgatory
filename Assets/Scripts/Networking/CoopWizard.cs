using Unity.Collections;
using UnityEngine;

internal sealed partial class CoopSession
{
    internal bool TryResolveWizardOrbContact(Vector3 start, Vector3 end, float localFraction, out float fraction)
    {
        fraction = localFraction;
        if (!InMatch || !IsHost || localBlock == null) return false;
        Peer target = null;
        foreach (var peer in peers.Values)
        {
            if (peer.Health <= 0) continue;
            var feet = ToWorld(peer.Position);
            feet.y = localBlock.position.y;
            if (localMovement != null && localMovement.TryGetGroundHeight(feet, out float y)) feet.y = y;
            float contact = Wizard.ContactFraction(start, end, feet);
            if (contact >= fraction) continue;
            fraction = contact;
            target = peer;
        }
        if (target == null) return false;
        if (!TryRegisterPeerDodge(target)) target.Health = Mathf.Max(0, target.Health - Wizard.OrbDamage);
        return true;
    }
    private static void WriteWizard(ref DataStreamWriter writer, Wizard.State state)
    {
        writer.WriteByte((byte)state.Phase);
        WriteVector(ref writer, state.Position);
        writer.WriteFloat(state.Yaw);
        writer.WriteFloat(state.Elapsed);
        writer.WriteInt(state.Health);
        writer.WriteUInt(state.HitSequence);
        writer.WriteULong(state.HitHistory);
        writer.WriteByte(state.ChargeAfterTeleport ? (byte)1 : (byte)0);
        WriteVector(ref writer, state.Destination);
        WriteWizardOrb(ref writer, state.First);
        WriteWizardOrb(ref writer, state.Second);
    }
    private static void WriteWizardOrb(ref DataStreamWriter writer, Wizard.OrbState orb)
    {
        writer.WriteByte((byte)orb.Status);
        WriteVector(ref writer, orb.Position);
        WriteVector(ref writer, orb.Direction);
        writer.WriteFloat(orb.Age);
        writer.WriteByte(orb.Target);
        writer.WriteUInt(orb.Id);
    }
    private static Wizard.State ReadWizard(ref DataStreamReader reader) => new Wizard.State
    {
        Phase = (Wizard.Phase)reader.ReadByte(), Position = ReadVector(ref reader),
        Yaw = reader.ReadFloat(), Elapsed = reader.ReadFloat(), Health = reader.ReadInt(),
        HitSequence = reader.ReadUInt(), HitHistory = reader.ReadULong(), ChargeAfterTeleport = reader.ReadByte() != 0,
        Destination = ReadVector(ref reader), First = ReadWizardOrb(ref reader), Second = ReadWizardOrb(ref reader)
    };
    private static Wizard.OrbState ReadWizardOrb(ref DataStreamReader reader) => new Wizard.OrbState
    {
        Status = (Wizard.OrbStatus)reader.ReadByte(), Position = ReadVector(ref reader), Direction = ReadVector(ref reader),
        Age = reader.ReadFloat(), Target = reader.ReadByte(), Id = reader.ReadUInt()
    };
    private static bool ValidWizardOrb(Wizard.OrbState orb) =>
        orb.Status <= Wizard.OrbStatus.Impact && ValidEnemyPosition(orb.Position) && ValidEnemyPosition(orb.Direction) &&
        float.IsFinite(orb.Age) && orb.Age >= 0f && orb.Age <= 13f && orb.Target <= 4;
}
