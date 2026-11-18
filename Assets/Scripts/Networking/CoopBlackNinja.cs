using Unity.Collections;
using UnityEngine;

internal sealed partial class CoopSession
{
    internal void ResolvePeersInBlackLane(LungeMan.AttackLane lane,int damage)
    {
        if(!InMatch || !IsHost)return;
        foreach(var peer in peers.Values)
            if(peer.Health>0 && lane.Contains(ToWorld(peer.Position)) && !TryRegisterPeerDodge(peer))peer.Health=Mathf.Max(0,peer.Health-damage);
    }
    private static void WriteBlackNinja(ref DataStreamWriter writer,BlackNinja.State state)
    {
        writer.WriteByte((byte)state.Phase);WriteVector(ref writer,state.Position);writer.WriteFloat(state.Yaw);writer.WriteFloat(state.Elapsed);writer.WriteInt(state.Health);
        writer.WriteUInt(state.HitSequence);writer.WriteULong(state.HitHistory);writer.WriteByte(state.Strike);
        WriteVector(ref writer,state.Origin);WriteVector(ref writer,state.Direction);writer.WriteFloat(state.Length);writer.WriteFloat(state.Opacity);writer.WriteUInt(state.AttackSequence);
    }
    private static BlackNinja.State ReadBlackNinja(ref DataStreamReader reader)=>new BlackNinja.State
    {
        Phase=(BlackNinja.Phase)reader.ReadByte(),Position=ReadVector(ref reader),Yaw=reader.ReadFloat(),Elapsed=reader.ReadFloat(),Health=reader.ReadInt(),
        HitSequence=reader.ReadUInt(),HitHistory=reader.ReadULong(),Strike=reader.ReadByte(),Origin=ReadVector(ref reader),Direction=ReadVector(ref reader),
        Length=reader.ReadFloat(),Opacity=reader.ReadFloat(),AttackSequence=reader.ReadUInt()
    };
}
