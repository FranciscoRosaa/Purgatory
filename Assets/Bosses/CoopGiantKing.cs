using Unity.Collections;
using UnityEngine;

internal sealed partial class CoopSession
{
    private const int GiantKingStateBytes = 79;
    internal void ResolvePeersInKingBox(Vector3 center,Quaternion rotation,Vector2 size,int damage)
    {
        if(!InMatch || !IsHost || localBlock==null) return;
        foreach(var peer in peers.Values)
        {
            if(peer.Health<=0)continue;
            var point=ToWorld(peer.Position);point.y=localBlock.position.y;
            if(GiantKing.Contains(point,center,rotation,size) && !TryRegisterPeerDodge(peer))
                peer.Health=Mathf.Max(0,peer.Health-damage);
        }
    }
    internal void ResolvePeersInKingSprint(GiantKing boss,Vector3 start,Vector3 end,Vector3 push)
    {
        if(!InMatch || !IsHost || localBlock==null) return;
        foreach(var peer in peers.Values)
        {
            if(peer.Health<=0)continue;
            var point=ToWorld(peer.Position);point.y=localBlock.position.y;
            if(!boss.SprintContact(start,end,point) || !boss.MarkSprintPeer(peer.Id))continue;
            if(TryRegisterPeerDodge(peer))continue;
            peer.SprintHit++;peer.SprintPush=push;
            peer.Health=Mathf.Max(0,peer.Health-boss.SprintDamage);
            if(avatars.TryGetValue(peer.Id,out var avatar)){avatar.SprintHit=peer.SprintHit;avatar.WhiteFlash.Pulse();}
        }
    }
    private static void WriteKing(ref DataStreamWriter writer,GiantKing.State state)
    {
        writer.WriteByte((byte)state.Phase);
        WriteVector(ref writer,state.Position);
        writer.WriteFloat(state.Yaw);writer.WriteFloat(state.Elapsed);writer.WriteInt(state.Health);
        writer.WriteByte(state.Enraged?(byte)1:(byte)0);
        writer.WriteUInt(state.HitSequence);writer.WriteULong(state.HitHistory);
        WriteVector(ref writer,state.Target);WriteVector(ref writer,state.Origin);
        writer.WriteUInt(state.AttackSequence);writer.WriteUInt(state.HazardSeed);writer.WriteFloat(state.HazardElapsed);
        writer.WriteByte(state.JumpNumber);writer.WriteUInt(state.ImpactSequence);
    }
    private static GiantKing.State ReadKing(ref DataStreamReader reader) => new GiantKing.State
    {
        Phase=(GiantKing.Phase)reader.ReadByte(),Position=ReadVector(ref reader),
        Yaw=reader.ReadFloat(),Elapsed=reader.ReadFloat(),Health=reader.ReadInt(),Enraged=reader.ReadByte()!=0,
        HitSequence=reader.ReadUInt(),HitHistory=reader.ReadULong(),Target=ReadVector(ref reader),Origin=ReadVector(ref reader),
        AttackSequence=reader.ReadUInt(),HazardSeed=reader.ReadUInt(),HazardElapsed=reader.ReadFloat(),
        JumpNumber=reader.ReadByte(),ImpactSequence=reader.ReadUInt()
    };
}
