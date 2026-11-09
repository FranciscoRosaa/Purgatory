using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(400)]
public sealed class MenuCharacterGrounding : MonoBehaviour
{
    public SkinnedMeshRenderer body;
    public float groundHeight = -0.07f;
    public bool followParentHeight;
    private Mesh baked;
    private CharacterAppearance appearance;
    private readonly List<Vector3> vertices = new List<Vector3>();

    private void LateUpdate() => SnapToGround();

    public void SnapToGround()
    {
        if (body == null) return;
        if (appearance == null) appearance = body.GetComponentInParent<CharacterAppearance>();
        if (baked == null) baked = new Mesh { name = "Character feet contact" };
        float bottom = float.PositiveInfinity;
        if (appearance != null && appearance.shoeSoles != null)
            foreach (var sole in appearance.shoeSoles)
                if (sole != null && sole.gameObject.activeInHierarchy)
                    bottom = Mathf.Min(bottom, Bottom(sole));
        if (float.IsInfinity(bottom)) bottom = Bottom(body);
        float target = followParentHeight && transform.parent != null ? transform.parent.position.y : groundHeight;
        if (!float.IsInfinity(bottom)) transform.position += Vector3.up * (target - bottom);
    }

    private float Bottom(SkinnedMeshRenderer renderer)
    {
        renderer.BakeMesh(baked, true);
        baked.GetVertices(vertices);
        float bottom = float.PositiveInfinity;
        foreach (var vertex in vertices)
            bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(vertex).y);
        return bottom;
    }

    private void OnDestroy()
    {
        if (baked != null) Destroy(baked);
    }
}
