using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal readonly struct CharacterProfile
{
    internal readonly string Name;
    internal readonly int Color;

    internal CharacterProfile(string name, int color)
    {
        Name = name;
        Color = color;
    }
}

internal static class CharacterProfiles
{
    internal const int SlotCount = 3;
    private const string Prefix = "Purgatory.Character.";
    private static readonly Color32[] Palette =
    {
        new Color32(255, 255, 255, 255),
        new Color32(245, 176, 99, 255),
        new Color32(97, 183, 244, 255),
        new Color32(97, 218, 166, 255),
        new Color32(172, 142, 239, 255),
        new Color32(242, 134, 153, 255)
    };

    internal static int ColorCount => Palette.Length;
    internal static int ActiveSlot => PlayerPrefs.GetInt(Prefix + "Active", -1);
    internal static CharacterProfile Active => Load(ActiveSlot);
    internal static bool HasSlot(int slot) => slot >= 0 && slot < SlotCount &&
        PlayerPrefs.HasKey(SlotKey(slot) + "Name");

    internal static CharacterProfile Load(int slot)
    {
        if (!HasSlot(slot))
            return new CharacterProfile("Personagem", 0);
        string key = SlotKey(slot);
        return new CharacterProfile(PlayerPrefs.GetString(key + "Name"),
            Mathf.Clamp(PlayerPrefs.GetInt(key + "Color"), 0, ColorCount - 1));
    }

    internal static void Save(int slot, CharacterProfile profile, bool activate)
    {
        string key = SlotKey(slot);
        PlayerPrefs.SetString(key + "Name", profile.Name);
        PlayerPrefs.SetInt(key + "Color", profile.Color);
        if (activate)
            PlayerPrefs.SetInt(Prefix + "Active", slot);
        PlayerPrefs.Save();
    }

    internal static Color Tint(int color) => Palette[Mathf.Clamp(color, 0, ColorCount - 1)];
    private static string SlotKey(int slot) => Prefix + slot + ".";

    internal static void ApplyTint(GameObject character, Color color)
    {
        var properties = new MaterialPropertyBlock();
        foreach (var renderer in character.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties);
        }
    }
}

internal sealed class CharacterCustomizer : MonoBehaviour
{
    private readonly Button[] slots = new Button[CharacterProfiles.SlotCount];
    private Outline[] swatches;
    private InputField nameField;
    private Text status, continueLabel;
    private int slot, color;
    private Action<CharacterProfile> preview;
    private Action continueToMode;
    private static readonly Color32 Gold = new Color32(245, 176, 99, 255);
    private static readonly Color32 Surface = new Color32(38, 48, 62, 255);
    private static readonly Color32 Ink = new Color32(12, 17, 24, 255);
    private static readonly Color32 White = new Color32(246, 237, 221, 255);

    internal void Setup(Action<CharacterProfile> showPreview, Action back)
    {
        preview = showPreview;
        nameField = transform.Find("Character Name").GetComponent<InputField>();
        nameField.textComponent.supportRichText = false;
        status = transform.Find("Status").GetComponent<Text>();
        continueLabel = transform.Find("Continue/Label").GetComponent<Text>();

        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            slots[i] = transform.Find("Slot " + (i + 1)).GetComponent<Button>();
            slots[i].onClick.AddListener(() => SelectSlot(index));
        }

        swatches = new Outline[CharacterProfiles.ColorCount];
        for (int i = 0; i < swatches.Length; i++)
        {
            int index = i;
            var button = transform.Find("Color " + i).GetComponent<Button>();
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = White;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.enabled = false;
            swatches[i] = outline;
            button.onClick.AddListener(() =>
            {
                color = index;
                UpdatePreview();
            });
        }

        nameField.onValueChanged.AddListener(_ => UpdatePreview());
        transform.Find("Save").GetComponent<Button>().onClick.AddListener(() => Save(false));
        transform.Find("Continue").GetComponent<Button>().onClick.AddListener(() =>
        {
            Save(true);
            continueToMode();
        });
        transform.Find("Back").GetComponent<Button>().onClick.AddListener(() => back());
    }

    internal void Open(string caption, Action next)
    {
        continueToMode = next;
        continueLabel.text = caption;
        gameObject.SetActive(true);
        SelectSlot(Mathf.Clamp(CharacterProfiles.ActiveSlot, 0, slots.Length - 1));
        EventSystem.current?.SetSelectedGameObject(slots[slot].gameObject);
    }

    private CharacterProfile Current()
    {
        string name = nameField.text.Trim();
        return new CharacterProfile(string.IsNullOrEmpty(name) ? "Personagem" : name, color);
    }

    private void SelectSlot(int index)
    {
        slot = index;
        var profile = CharacterProfiles.Load(slot);
        color = profile.Color;
        nameField.SetTextWithoutNotify(CharacterProfiles.HasSlot(slot) ? profile.Name : "");
        RefreshSlots();
        UpdatePreview();
        status.text = CharacterProfiles.HasSlot(slot) ? "Loaded saved character." :
            "Empty slot. Choose a name and color.";
    }

    private void Save(bool activate)
    {
        CharacterProfiles.Save(slot, Current(), activate);
        RefreshSlots();
        status.text = "Character saved to slot " + (slot + 1) + ".";
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var label = slots[i].GetComponentInChildren<Text>();
            string name = CharacterProfiles.HasSlot(i) ? CharacterProfiles.Load(i).Name : "Empty";
            if (name.Length > 9)
                name = name.Substring(0, 9) + "...";
            label.supportRichText = false;
            label.text = (i + 1) + " / " + name;
            label.fontSize = 18;
            label.color = i == slot ? Ink : White;
            slots[i].image.color = i == slot ? Gold : Surface;
        }
    }

    private void UpdatePreview()
    {
        for (int i = 0; i < swatches.Length; i++)
            swatches[i].enabled = i == color;
        preview(Current());
    }
}
