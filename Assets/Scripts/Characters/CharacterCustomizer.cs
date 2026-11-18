using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal readonly struct CharacterProfile
{
    internal readonly string Name;
    internal readonly int Pants, Shoes, Nose, Eyes, Hair;
    internal byte Appearance => (byte)((1 - Pants) | Shoes << 1 | Nose << 2 | Eyes << 3 | Hair << 4);

    internal CharacterProfile(string name, int pants = 1, int shoes = 1, int nose = 1, int eyes = 1, int hair = 1)
    {
        Name = name;
        Pants = Mathf.Clamp(pants, 0, 1);
        Shoes = Mathf.Clamp(shoes, 0, 1);
        Nose = Mathf.Clamp(nose, 0, 1);
        Eyes = Mathf.Clamp(eyes, 0, 1);
        Hair = Mathf.Clamp(hair, 0, 1);
    }

    internal static CharacterProfile FromAppearance(string name, byte appearance)
    {
        if (appearance >= CharacterProfiles.AppearanceCount) throw new ArgumentOutOfRangeException(nameof(appearance));
        return new CharacterProfile(name, 1 - (appearance & 1), (appearance >> 1) & 1,
            (appearance >> 2) & 1, (appearance >> 3) & 1, (appearance >> 4) & 1);
    }
}

internal static class CharacterProfiles
{
    internal const int SlotCount = 3, AppearanceCount = 32;
    private const string Prefix = "Purgatory.Character.";
    internal static int ActiveSlot => PlayerPrefs.GetInt(Prefix + "Active", -1);
    internal static CharacterProfile Active => Load(ActiveSlot);
    internal static bool HasSlot(int slot) => slot >= 0 && slot < SlotCount &&
        PlayerPrefs.HasKey(SlotKey(slot) + "Name");

    internal static CharacterProfile Load(int slot)
    {
        if (!HasSlot(slot)) return new CharacterProfile("Character");
        string key = SlotKey(slot);
        return new CharacterProfile(PlayerPrefs.GetString(key + "Name"),
            PlayerPrefs.GetInt(key + "Pants", 1), PlayerPrefs.GetInt(key + "Shoes", 1),
            PlayerPrefs.GetInt(key + "Nose", 1), PlayerPrefs.GetInt(key + "Eyes", 1),
            PlayerPrefs.GetInt(key + "Hair", 1));
    }

    internal static void Save(int slot, CharacterProfile profile, bool activate)
    {
        if (slot < 0 || slot >= SlotCount) return;
        string key = SlotKey(slot);
        PlayerPrefs.SetString(key + "Name", profile.Name);
        PlayerPrefs.SetInt(key + "Pants", profile.Pants);
        PlayerPrefs.SetInt(key + "Shoes", profile.Shoes);
        PlayerPrefs.SetInt(key + "Nose", profile.Nose);
        PlayerPrefs.SetInt(key + "Eyes", profile.Eyes);
        PlayerPrefs.SetInt(key + "Hair", profile.Hair);
        if (activate) PlayerPrefs.SetInt(Prefix + "Active", slot);
        PlayerPrefs.Save();
    }

    private static string SlotKey(int slot) => Prefix + slot + ".";

    internal static void ApplyAppearance(GameObject character, CharacterProfile profile)
    {
        if (character == null) return;
        foreach (var appearance in character.GetComponentsInChildren<CharacterAppearance>(true))
            appearance.Apply(profile.Pants, profile.Shoes, profile.Nose, profile.Eyes, profile.Hair);
    }
}

internal sealed class CharacterCustomizer : MonoBehaviour
{
    private readonly Button[] slots = new Button[CharacterProfiles.SlotCount];
    private readonly int[] choices = new int[7];
    private static readonly int[] Categories = { 0, 1, 4, 5, 6 };
    private static readonly string[] Equipped = { "Leather shoes", "Medieval pants", "", "", "Classic nose", "Brown eyes & brows", "Short brown hair" };
    private static readonly string[] Unequipped = { "Barefoot", "No pants", "", "", "Base face", "Base face", "Bald" };
    private InputField nameField;
    private Text status;
    private int slot;
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

        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            slots[i] = transform.Find("Slot " + (i + 1)).GetComponent<Button>();
            slots[i].onClick.AddListener(() => SelectSlot(index));
        }
        foreach (int category in Categories)
        {
            int index = category;
            foreach (var name in new[] { "Previous", "Next" })
            {
                var button = transform.Find("Category " + index + "/" + name).GetComponent<Button>();
                button.interactable = true;
                button.onClick.AddListener(() => { choices[index] = 1 - choices[index]; UpdatePreview(); });
            }
        }
        var presentation = FindAnyObjectByType<WorldMenuPresentation>();
        transform.Find("Rotate Left").GetComponent<Button>().onClick.AddListener(() => presentation.RotateCharacter(-45f));
        transform.Find("Rotate Right").GetComponent<Button>().onClick.AddListener(() => presentation.RotateCharacter(45f));
        nameField.onValueChanged.AddListener(_ => UpdatePreview());
        transform.Find("Save").GetComponent<Button>().onClick.AddListener(Save);
        transform.Find("Continue").GetComponent<Button>().onClick.AddListener(() => { Save(); continueToMode(); });
        transform.Find("Back").GetComponent<Button>().onClick.AddListener(() => back());
    }

    internal void Open(string caption, Action next)
    {
        continueToMode = next;
        gameObject.SetActive(true);
        FindAnyObjectByType<WorldMenuPresentation>().ResetCharacterRotation();
        SelectSlot(Mathf.Clamp(CharacterProfiles.ActiveSlot, 0, slots.Length - 1));
        EventSystem.current?.SetSelectedGameObject(slots[slot].gameObject);
    }

    private CharacterProfile Current()
    {
        string name = nameField.text.Trim();
        return new CharacterProfile(string.IsNullOrEmpty(name) ? "Character" : name,
            choices[1], choices[0], choices[4], choices[5], choices[6]);
    }

    private void SelectSlot(int index)
    {
        slot = index;
        var profile = CharacterProfiles.Load(slot);
        choices[0] = profile.Shoes;
        choices[1] = profile.Pants;
        choices[4] = profile.Nose;
        choices[5] = profile.Eyes;
        choices[6] = profile.Hair;
        nameField.SetTextWithoutNotify(CharacterProfiles.HasSlot(slot) ? profile.Name : "");
        RefreshSlots();
        UpdatePreview();
        status.text = "Choose your clothing and features.\nChanges preview instantly.";
    }

    private void Save()
    {
        CharacterProfiles.Save(slot, Current(), true);
        RefreshSlots();
        status.text = "Character saved to slot " + (slot + 1) + ".";
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var label = slots[i].GetComponentInChildren<Text>();
            string name = CharacterProfiles.HasSlot(i) ? CharacterProfiles.Load(i).Name : "Empty";
            if (name.Length > 7) name = name.Substring(0, 7) + "...";
            label.supportRichText = false;
            label.text = (i + 1) + " / " + name;
            label.fontSize = 15;
            label.color = i == slot ? Ink : White;
            slots[i].image.color = i == slot ? Gold : Surface;
        }
    }

    private void UpdatePreview()
    {
        foreach (int category in Categories)
            transform.Find("Category " + category + "/Value").GetComponent<Text>().text =
                choices[category] != 0 ? Equipped[category] : Unequipped[category];
        preview(Current());
    }
}
