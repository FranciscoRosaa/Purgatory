using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

internal sealed partial class CoopSession
{
    private const int LibraryHazardBytes = 20;
    private uint lastLibraryHazard, libraryHazardAcknowledgement;

    internal void TriggerPeerLibraryHazard(LibraryHazard hazard, float now)
    {
        if (!InMatch || !IsHost) return;
        foreach (var peer in peers.Values)
        {
            if (peer.Health <= 0 || !hazard.TryChooseDestination(ToWorld(peer.Position), out var point)) continue;
            peer.LibraryHazardSequence++;
            if (peer.LibraryHazardSequence == 0) peer.LibraryHazardSequence++;
            peer.LibraryHazardDestination = point;
            peer.LibraryHazardStarted = now;
            peer.LibraryHazardApplied = false;
        }
    }

    private void ApplyPeerLibraryHazards(float now)
    {
        foreach (var peer in peers.Values)
        {
            if (peer.LibraryHazardSequence == 0 || peer.LibraryHazardApplied ||
                now - peer.LibraryHazardStarted < LibraryHazard.FlashLead) continue;
            peer.LibraryHazardApplied = true;
            peer.Position = new Vector2(peer.LibraryHazardDestination.x, peer.LibraryHazardDestination.z);
            peer.DashStartedAt = float.NegativeInfinity;
            peer.PendingLunges.Clear();
            peer.MeleeArmed = false;
            if (avatars.TryGetValue(peer.Id, out var avatar) && avatar.Object != null)
                avatar.Object.transform.position = peer.LibraryHazardDestination;
        }
    }

    private static void WriteLibraryHazard(ref DataStreamWriter writer, Peer peer)
    {
        writer.WriteUInt(peer.LibraryHazardSequence);
        WriteVector(ref writer, peer.LibraryHazardDestination);
        writer.WriteFloat(peer.LibraryHazardSequence == 0 ? 0f : Mathf.Max(0f, Time.time - peer.LibraryHazardStarted));
    }

    private bool ReadLibraryHazard(ref DataStreamReader reader)
    {
        uint sequence = reader.ReadUInt();
        Vector3 point = ReadVector(ref reader);
        float elapsed = reader.ReadFloat();
        if (!ValidEnemyPosition(point) || !float.IsFinite(elapsed) || elapsed < 0f) return false;
        if (sequence == 0 || sequence == lastLibraryHazard) return true;
        var hazard = LibraryWorld.Active != null ? LibraryWorld.Active.GetComponent<LibraryHazard>() : null;
        if (hazard == null) return true;
        lastLibraryHazard = sequence;
        hazard.BeginFlash(point, elapsed, sequence);
        return true;
    }

    internal void AcknowledgeLibraryHazard(uint sequence)
    {
        if (sequence == lastLibraryHazard) libraryHazardAcknowledgement = sequence;
    }

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
