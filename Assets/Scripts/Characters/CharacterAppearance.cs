using UnityEngine;

public sealed class CharacterAppearance : MonoBehaviour
{
    public GameObject medievalPants, medievalShoes, nose, eyes, hair;
    public SkinnedMeshRenderer body;
    public SkinnedMeshRenderer[] shoeSoles;
    public Mesh bareBody, clothedBody, shoesBody, fullyClothedBody;
    public static readonly Color Skin = new Color32(210, 161, 127, 255);

    private void Awake() => SetSkin();

    public void Apply(int pants, int shoes = 1, int noseChoice = 1, int eyesChoice = 1, int hairChoice = 1)
    {
        SetSkin();
        if (body != null)
        {
            var mesh = pants != 0 ? (shoes != 0 ? fullyClothedBody : clothedBody) :
                (shoes != 0 ? shoesBody : bareBody);
            if (mesh != null) body.sharedMesh = mesh;
        }
        if (medievalPants != null) medievalPants.SetActive(pants != 0);
        if (medievalShoes != null) medievalShoes.SetActive(shoes != 0);
        if (nose != null) nose.SetActive(noseChoice != 0);
        if (eyes != null) eyes.SetActive(eyesChoice != 0);
        if (hair != null) hair.SetActive(hairChoice != 0);
    }

    private void SetSkin()
    {
        if (body == null) return;
        var properties = new MaterialPropertyBlock();
        body.GetPropertyBlock(properties);
        properties.SetColor("_BaseColor", Skin);
        body.SetPropertyBlock(properties);
    }
}
