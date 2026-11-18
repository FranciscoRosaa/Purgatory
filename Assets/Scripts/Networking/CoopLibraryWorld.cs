using System.Collections.Generic;
using UnityEngine;

internal sealed partial class CoopSession
{
    internal void AddLibraryFocuses(List<Vector3> points)
    {
        if(!InMatch)return;
        foreach(var avatar in avatars.Values)if(avatar.Object!=null)points.Add(avatar.Object.transform.position);
    }
    internal void EnterLibrary(Vector3 center)
    {
        Vector3 Position(byte id)=>center+Vector3.right*(Mathf.Max(1,(int)id)-1)*2f;
        if(localMovement!=null)localMovement.WarpTo(Position(localId));
        if(IsHost)
            foreach(var peer in peers.Values)
            {
                var p=Position(peer.Id);peer.Position=new Vector2(p.x,p.z);
                peer.DashStartedAt=float.NegativeInfinity;peer.PendingLunges.Clear();
                if(avatars.TryGetValue(peer.Id,out var avatar) && avatar.Object!=null)avatar.Object.transform.position=p;
            }
    }
}
