using System.Collections.Generic;
using UnityEngine;

// Scroll wheel switches between what you hold: empty hands, the gun, and any items you add later.
// Number keys pick directly: 1 = gun, 2, 3... = your extra items.
// Put this on the PLAYER.
public class WeaponSwitcher : MonoBehaviour
{
    public static WeaponSwitcher Instance;

    [System.Serializable]
    public class Item
    {
        public string name = "Item";
        [Tooltip("The object shown in your hand while this item is selected (hidden otherwise).")]
        public GameObject model;
        [Tooltip("Optional: a script that only works while this item is selected (switched off otherwise).")]
        public Behaviour script;
    }

    [Tooltip("Your gun script. Leave empty to find it automatically.")]
    public PlayerGun gun;
    public string gunName = "Revolver";
    public string emptyHandsName = "Empty hands";
    [Tooltip("Extra things to hold later (lantern, photo, key...). They appear after the gun when scrolling.")]
    public List<Item> items = new List<Item>();

    const float SwitchCooldown = 0.15f;  // so one flick of the wheel = one switch

    // 0 = empty hands, 1 = gun, 2+ = items
    public int Current { get; private set; }
    public string CurrentName { get { return NameOf(Current); } }

    int Count { get { return 2 + items.Count; } }
    float lastSwitch;
    string shownName;
    float shownTimer;

    void Awake() { Instance = this; }

    void Start()
    {
        if (gun == null) gun = GetComponentInChildren<PlayerGun>();
        if (gun == null) gun = FindObjectOfType<PlayerGun>();
        Select(gun != null ? 1 : 0, false);
    }

    void Update()
    {
        // Scroll wheel: down = next, up = previous (wraps around)
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.1f && Time.time - lastSwitch > SwitchCooldown)
        {
            int next = Current + (scroll < 0f ? 1 : -1);
            if (next >= Count) next = 0;
            if (next < 0) next = Count - 1;
            if (next == 1 && gun == null) next = scroll < 0f ? (Count > 2 ? 2 : 0) : 0;
            Select(next, true);
        }

        // Number keys: 1 = gun, 2.. = items
        for (int k = 0; k < Mathf.Min(9, Count - 1); k++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + k)) Select(k + 1, true);

        shownTimer -= Time.deltaTime;
    }

    public void Select(int index, bool announce)
    {
        index = Mathf.Clamp(index, 0, Count - 1);
        Current = index;
        lastSwitch = Time.time;

        if (gun != null) gun.holstered = index != 1;

        for (int i = 0; i < items.Count; i++)
        {
            bool on = index == i + 2;
            if (items[i].model != null) items[i].model.SetActive(on);
            if (items[i].script != null) items[i].script.enabled = on;
        }

        // (No name text on screen when switching)
    }

    string NameOf(int index)
    {
        if (index == 0) return emptyHandsName;
        if (index == 1) return gunName;
        int i = index - 2;
        return i < items.Count ? items[i].name : "";
    }

    // Backup item name when the UI doesn't have a label for it
    void OnGUI()
    {
        if (shownTimer <= 0f || Event.current.type != EventType.Repaint) return;
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.alignment = TextAnchor.MiddleCenter;
        st.fontStyle = FontStyle.Italic;
        st.fontSize = Mathf.RoundToInt(15f * Screen.height / 1080f);
        st.normal.textColor = new Color(0.86f, 0.83f, 0.78f, Mathf.Clamp01(shownTimer / 0.6f) * 0.75f);
        GUI.Label(new Rect(0, Screen.height - 100f * Screen.height / 1080f, Screen.width, 30), shownName, st);
    }
}