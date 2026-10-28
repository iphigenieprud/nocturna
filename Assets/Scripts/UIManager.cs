using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// The only script that touches the screen UI.
// Other scripts just tell it what happened (ammo changed, hit, ending...),
// and you can restyle everything in the Canvas without touching any code.
//
// Quick start: put this on an empty object, click the three dots on this component
// and choose "Build Default UI". A ready-made Canvas appears, already hooked up.
public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Ammo")]
    public TMP_Text ammoText;
    public TMP_Text ammoMaxText;
    [Tooltip("Holds the bullet icons. Needs a Horizontal Layout Group.")]
    public RectTransform bulletRow;
    [Tooltip("One bullet icon (switched off). It gets copied once per bullet.")]
    public Image bulletTemplate;

    [Header("Ammo Colors")]
    public Color ammoNormal = new Color(0.86f, 0.83f, 0.78f, 0.8f);
    public Color ammoLow = new Color(0.85f, 0.65f, 0.45f, 0.85f);
    public Color ammoEmpty = new Color(0.7f, 0.2f, 0.18f, 0.9f);
    public Color bulletFull = new Color(0.86f, 0.83f, 0.78f, 0.7f);
    public Color bulletSpent = new Color(0.86f, 0.83f, 0.78f, 0.12f);

    [Header("Reload")]
    public GameObject reloadGroup;
    [Tooltip("The bar that fills up. Anchored to the left; its width grows as you reload.")]
    public RectTransform reloadFill;
    [Tooltip("Shown when the gun is empty (blinks).")]
    public TMP_Text reloadPrompt;

    [Header("Crosshair")]
    public RectTransform crosshair;
    public RectTransform crossTop, crossBottom, crossLeft, crossRight;
    [Tooltip("Smallest gap between the lines and the center.")]
    public float crosshairGap = 4f;
    [Tooltip("How faded the crosshair is while aiming (0 = invisible, 1 = no change).")]
    [Range(0f, 1f)] public float aimFade = 0.6f;

    [Header("Hit Marker")]
    public CanvasGroup hitMarker;
    public float hitMarkerTime = 0.25f;

    [Header("Health Bar")]
    [Tooltip("The whole health bar (it shakes when she hits you).")]
    public RectTransform healthBar;
    [Tooltip("The filled part. Anchored to the left; its width shrinks as you lose health.")]
    public Image healthFill;
    [Tooltip("Shows the health you just lost, then slides down to catch up.")]
    public RectTransform healthLag;
    public Color healthColor = new Color(0.8f, 0.77f, 0.72f, 0.75f);
    [Tooltip("The bar turns this color when you're low.")]
    public Color lowHealthColor = new Color(0.6f, 0.15f, 0.12f, 0.85f);

    [Header("Stamina Bar")]
    [Tooltip("The whole stamina bar. It fades out when stamina is full.")]
    public CanvasGroup staminaBar;
    [Tooltip("The filled part. Anchored to the left; its width shrinks as you use stamina.")]
    public Image staminaFill;
    public Color staminaColor = new Color(0.7f, 0.72f, 0.75f, 0.6f);
    [Tooltip("Flashes this color when you try to dodge without enough stamina.")]
    public Color noStaminaColor = new Color(0.65f, 0.2f, 0.18f, 0.9f);

    [Header("Damage Flashes")]
    [Tooltip("Full-screen image: flashes red when she hits you.")]
    public Image damageOverlay;
    public Color damageColor = new Color(0.5f, 0f, 0f, 1f);
    [Tooltip("Full-screen image: glows warm when lighting a candle hurts you.")]
    public Image memoryOverlay;
    public Color memoryColor = new Color(1f, 0.8f, 0.5f, 1f);

    [Header("Quiet HUD")]
    [Tooltip("Parts of the HUD fade away when you don't need them, so they don't break the atmosphere.")]
    public bool quietHud = true;
    [Tooltip("The ammo counter (fades out when you're not shooting or aiming).")]
    public CanvasGroup ammoGroup;
    [Tooltip("The health bar (fades out when nothing is happening to your health).")]
    public CanvasGroup healthGroup;

    [Header("Dark Souls Style")]
    [Tooltip("Dark banners with thin fading lines behind prompts and memories, framed red health and green stamina bars at the top left, and wide banner endings. Applied when the game starts.")]
    public bool soulsStyle = true;

    [Header("Interaction Prompt Look")]
    [Tooltip("Makes the [E] hint, the charge circle and memory lines bigger and brighter, with a soft dark glow behind them.")]
    public bool strongPrompts = true;

    [Header("Hold Ring")]
    [Tooltip("The circle that fills while you hold E.")]
    public Image holdRing;
    [Tooltip("The faint full circle behind it.")]
    public Image holdRingBack;

    [Header("Counters")]
    [Tooltip("Lit candles, e.g. 'candles   3 / 6'.")]
    public TMP_Text candleCounter;
    [Tooltip("Memories found, e.g. 'memories   1 / 5'.")]
    public TMP_Text mementoCounter;
    [Tooltip("How visible the counters are when nothing has changed (they brighten when a number changes).")]
    [Range(0f, 1f)] public float counterIdleAlpha = 0.35f;

    [Header("Prompt")]
    [Tooltip("Small hint near the bottom of the screen, like '[E]  remember'.")]
    public TMP_Text promptLabel;

    [Header("Item Name")]
    [Tooltip("Shows the name of what you switched to (scroll wheel), then fades.")]
    public TMP_Text itemLabel;

    [Header("Ending Screen")]
    public CanvasGroup endingPanel;
    public TMP_Text endingTitle;
    public TMP_Text endingLine;
    public TMP_Text restartHint;
    public float endingFadeTime = 1f;

    [Header("Everything Shown During Play")]
    [Tooltip("Hidden when an ending starts.")]
    public CanvasGroup hud;

    readonly List<Image> bullets = new List<Image>();
    CanvasGroup crosshairGroup;
    float hitTimer;
    float damageFlash, memoryFlash;
    float healthShown = 1f, lagShown = 1f, healthTarget = 1f;
    float shake;
    const float IdleDelay = 2.5f;   // seconds before an unused part of the HUD fades
    float ammoActivity = -99f, healthActivity = -99f, shotTime = -99f;
    float ammoAlpha, healthAlpha, linesAlpha;
    bool lowAmmo;
    bool weaponOut = true;
    float itemTimer;
    Object promptOwner;
    Object holdOwner;
    float holdProgress, holdAlpha;
    int lastLit = -1, lastCandles = -1, lastFound = -1, lastMementos = -1;
    float counterChanged = -99f;
    float promptAlpha;
    float staminaShown = 1f, staminaTarget = 1f, noStaminaFlash, staminaVisible;
    Vector2 staminaHome;
    Vector2 barHome;
    float endingFade = -1f;
    bool empty;
    int lastAmmo = -1, lastMax = -1;

    void Awake()
    {
        if (soulsStyle) ApplySoulsStyle();
        if (healthBar != null) barHome = healthBar.anchoredPosition;

        // Older UI without the circle: add it now, next to the prompt
        if (holdRing == null && promptLabel != null) MakeRings(promptLabel.transform.parent);
        if (strongPrompts) StrengthenPrompts();
        if (soulsStyle) BuildSoulsBanners();

        foreach (Image ring in new[] { holdRing, holdRingBack })
        {
            if (ring == null) continue;
            if (ring.sprite == null) ring.sprite = RingSprite();
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
        }
        if (holdRingBack != null) holdRingBack.fillAmount = 1f;
        if (staminaBar != null)
        {
            staminaHome = ((RectTransform)staminaBar.transform).anchoredPosition;
            staminaBar.alpha = 0f;
        }

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("There are two UIManagers in the scene. Removing this one.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (crosshair != null)
        {
            crosshairGroup = crosshair.GetComponent<CanvasGroup>();
            if (crosshairGroup == null) crosshairGroup = crosshair.gameObject.AddComponent<CanvasGroup>();
        }
        if (bulletTemplate != null) bulletTemplate.gameObject.SetActive(false);
        if (hitMarker != null) hitMarker.alpha = 0f;
        if (reloadGroup != null) reloadGroup.SetActive(false);
        if (reloadPrompt != null) reloadPrompt.gameObject.SetActive(false);
        if (endingPanel != null)
        {
            endingPanel.alpha = 0f;
            endingPanel.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // Hit marker fades out
        if (hitMarker != null)
        {
            hitTimer -= Time.deltaTime;
            hitMarker.alpha = Mathf.Clamp01(hitTimer / hitMarkerTime);
        }

        UpdateHealthBar();
        UpdateQuietHud();

        // Red flash when she hits you, warm glow when a candle hurts you
        damageFlash = Mathf.Max(0f, damageFlash - Time.deltaTime * 2f);
        memoryFlash = Mathf.Max(0f, memoryFlash - Time.deltaTime * 0.8f);
        if (damageOverlay != null)
        {
            Color c = damageColor;
            c.a = damageFlash * 0.5f;
            damageOverlay.color = c;
        }
        if (memoryOverlay != null)
        {
            Color c = memoryColor;
            c.a = Mathf.Sin(Mathf.Clamp01(memoryFlash) * Mathf.PI) * 0.3f; // swells in, then fades
            memoryOverlay.color = c;
        }

        // "RELOAD" blinks while the gun is empty
        if (reloadPrompt != null && empty)
        {
            Color c = reloadPrompt.color;
            c.a = 0.4f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            reloadPrompt.color = c;
        }

        // Ending screen fades in
        if (endingFade >= 0f && endingPanel != null)
        {
            endingFade += Time.deltaTime;
            endingPanel.alpha = endingFadeTime > 0f ? Mathf.Clamp01(endingFade / endingFadeTime) : 1f;
            if (hud != null) hud.alpha = 1f - endingPanel.alpha;
        }
    }

    // Only show what matters right now
    void UpdateQuietHud()
    {
        float dt = Time.deltaTime;
        float now = Time.time;

        float ammoWant = !weaponOut ? 0f
                       : !quietHud || now - ammoActivity < IdleDelay || empty || lowAmmo ? 1f : 0f;
        float healthWant = !quietHud || now - healthActivity < IdleDelay ? 1f
                         : healthTarget < 0.999f ? 0.35f : 0f;   // stays faintly visible while you're hurt
        float linesWant = !quietHud || aimingNow || now - shotTime < 0.8f ? 1f : 0f;

        ammoAlpha = Mathf.MoveTowards(ammoAlpha, ammoWant, dt * (ammoWant > ammoAlpha ? 6f : 1f));
        healthAlpha = Mathf.MoveTowards(healthAlpha, healthWant, dt * (healthWant > healthAlpha ? 6f : 1f));
        linesAlpha = Mathf.MoveTowards(linesAlpha, linesWant, dt * (linesWant > linesAlpha ? 10f : 2f));

        if (ammoGroup != null) ammoGroup.alpha = ammoAlpha;

        UpdateCounters();

        // Charge ring: an empty circle appears with the prompt (it slowly breathes, inviting you to fill it),
        // and fills while E is kept down
        holdAlpha = Mathf.MoveTowards(holdAlpha, holdOwner != null ? 1f : 0f, dt * 6f);
        float ringShown = Mathf.Max(promptAlpha, holdAlpha);
        if (holdRing != null)
        {
            holdRing.fillAmount = holdProgress;
            Color c = holdRing.color; c.a = holdAlpha * (strongPrompts ? 1f : 0.85f); holdRing.color = c;
        }
        if (holdRingBack != null)
        {
            float breathe = strongPrompts ? 0.45f + 0.15f * Mathf.Sin(Time.time * 2.5f)
                                          : 0.22f + 0.1f * Mathf.Sin(Time.time * 2.5f);
            float s = 1f + 0.06f * Mathf.Sin(Time.time * 2.5f) * (1f - holdAlpha);
            holdRingBack.rectTransform.localScale = Vector3.one * s;
            Color c = holdRingBack.color; c.a = ringShown * breathe; holdRingBack.color = c;
        }

        // Prompt fades in and out softly
        if (promptLabel != null)
        {
            promptAlpha = Mathf.MoveTowards(promptAlpha, promptOwner != null ? 1f : 0f, dt * 3f);
            Color pc = promptLabel.color;
            pc.a = promptAlpha * (strongPrompts ? 0.85f + 0.15f * Mathf.Sin(Time.time * 2f)
                                                : 0.55f + 0.1f * Mathf.Sin(Time.time * 2f));
            if (promptBack != null) { Color bc = promptBack.color; bc.a = Mathf.Max(promptAlpha, holdAlpha) * 0.55f; promptBack.color = bc; }
            if (promptBanner != null) promptBanner.alpha = Mathf.Max(promptAlpha, holdAlpha);
            promptLabel.color = pc;
        }

        // Item name fades out after a moment
        if (itemLabel != null)
        {
            itemTimer -= dt;
            Color ic = itemLabel.color;
            ic.a = Mathf.Clamp01(itemTimer / 0.6f) * (strongPrompts ? 0.95f : 0.75f);
            if (itemBack != null) { Color bc = itemBack.color; bc.a = Mathf.Clamp01(itemTimer / 0.6f) * 0.55f; itemBack.color = bc; }
            if (itemBanner != null) itemBanner.alpha = Mathf.Clamp01(itemTimer / 0.6f);
            itemLabel.color = ic;
        }
        if (healthGroup != null) healthGroup.alpha = healthAlpha;

        // Crosshair: just a small dot, the lines only show while aiming or shooting
        foreach (RectTransform r in new[] { crossTop, crossBottom, crossLeft, crossRight })
        {
            if (r == null) continue;
            Graphic g = r.GetComponent<Graphic>();
            if (g == null) continue;
            Color c = g.color;
            c.a = linesAlpha * 0.7f;
            g.color = c;
        }
    }

    bool aimingNow;

    // Is the gun out? (false = empty hands or another item: the ammo counter hides)
    public void SetWeaponOut(bool isOut)
    {
        if (isOut && !weaponOut) ammoActivity = Time.time; // show the ammo briefly when it comes out
        weaponOut = isOut;
    }

    public bool HasItemLabel { get { return itemLabel != null; } }

    public void ShowItemName(string itemName)
    {
        if (itemLabel == null) return;
        itemLabel.text = itemName;
        itemTimer = 1.8f;
    }

    // Candles lit and memories found. They glow brighter for a moment when a number changes.
    void UpdateCounters()
    {
        int lit = 0, candles = 0;
        foreach (Candle c in Candle.All) { if (c == null) continue; candles++; if (c.IsLit) lit++; }
        int found = Memento.CollectedCount, mementos = Memento.Total;
        if (Memento.All.Count == 0) mementos = 0; // hidden until the first memory appears

        if (lit != lastLit || candles != lastCandles || found != lastFound || mementos != lastMementos)
        {
            if (lastLit >= 0) counterChanged = Time.time;
            lastLit = lit; lastCandles = candles; lastFound = found; lastMementos = mementos;
            if (candleCounter != null) candleCounter.text = "candles   " + lit + " / " + candles;
            if (mementoCounter != null) mementoCounter.text = mementos > 0 ? "memories   " + found + " / " + mementos : "";
        }

        float since = Time.time - counterChanged;
        float a = since < 2.5f ? 0.85f : Mathf.Lerp(0.85f, counterIdleAlpha, Mathf.Clamp01((since - 2.5f) / 1.5f));
        SetAlpha(candleCounter, a);
        SetAlpha(mementoCounter, a);
    }

    void SetAlpha(TMP_Text t, float a)
    {
        if (t == null) return;
        Color c = t.color;
        c.a = a;
        t.color = c;
    }

    public bool HasPrompt { get { return promptLabel != null; } }

    // Fills the circle (0 to 1) while you hold E. 0 hides it.
    public void SetHold(Object owner, float progress)
    {
        if (progress <= 0f)
        {
            if (holdOwner == owner) { holdOwner = null; holdProgress = 0f; }
            return;
        }
        holdOwner = owner;
        holdProgress = progress;
    }

    // Shows a hint. The owner is whoever asked (so two things don't fight over it).
    public void ShowPrompt(Object owner, string text) { ShowPrompt(owner, text, null); }

    // title = what the thing is (shown on top), text = what to do (shown under it, with the circle beside it)
    public void ShowPrompt(Object owner, string text, string title)
    {
        if (promptLabel == null) return;
        string full = string.IsNullOrEmpty(title) ? text
                    : "<size=115%>" + title + "</size>\n<size=85%><alpha=#BB>" + text + "</size>";
        if (promptOwner != owner || promptLabel.text != full)
        {
            promptLabel.text = full;
            promptInstruction = text;
            promptHasTitle = !string.IsNullOrEmpty(title);
            PlaceRings();
        }
        promptOwner = owner;
    }

    string promptInstruction = "";
    bool promptHasTitle;

    // The circle sits just left of the instruction line
    void PlaceRings()
    {
        FitBanner(promptBanner, promptLabel, promptHasTitle ? 92f : 62f);
        if (!strongPrompts || promptLabel == null) return;
        RectTransform pr = promptLabel.rectTransform;
        float lineWidth = promptLabel.GetPreferredValues(promptInstruction).x * (promptHasTitle ? 0.85f : 1f);
        float lineHeight = promptLabel.fontSize * 1.2f;
        float centerY = pr.anchoredPosition.y + (0.5f - pr.pivot.y) * pr.sizeDelta.y;
        float y = promptHasTitle ? centerY - lineHeight * 0.5f : centerY;
        float x = pr.anchoredPosition.x + (0.5f - pr.pivot.x) * pr.sizeDelta.x - lineWidth * 0.5f - 34f;

        foreach (Image ring in new[] { holdRingBack, holdRing })
        {
            if (ring == null) continue;
            RectTransform rr = ring.rectTransform;
            rr.anchorMin = pr.anchorMin;
            rr.anchorMax = pr.anchorMax;
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = new Vector2(x, y);
            rr.sizeDelta = new Vector2(40f, 40f);
        }
    }

    public void HidePrompt(Object owner)
    {
        if (promptOwner == owner) promptOwner = null;
    }

    // A memory's line when you pick up a Memento (stays a bit longer)
    public void ShowMemory(string memoryLine)
    {
        if (itemLabel == null) return;
        itemLabel.text = memoryLine;
        itemTimer = 4.5f;
        FitBanner(itemBanner, itemLabel, 96f);
    }

    // ---------- Called by other scripts ----------

    public void SetAmmo(int ammo, int max)
    {
        if (ammo == lastAmmo && max == lastMax) return;

        if (max != lastMax) BuildBullets(max);
        if (lastAmmo >= 0 && ammo < lastAmmo) shotTime = Time.time;
        ammoActivity = Time.time;
        lastAmmo = ammo;
        lastMax = max;

        bool low = ammo <= Mathf.Max(1, max / 4);
        lowAmmo = low;
        if (ammoText != null)
        {
            ammoText.text = ammo.ToString();
            ammoText.color = ammo == 0 ? ammoEmpty : low ? ammoLow : ammoNormal;
        }
        if (ammoMaxText != null) ammoMaxText.text = "/ " + max;

        for (int i = 0; i < bullets.Count; i++)
            bullets[i].color = i < ammo ? bulletFull : bulletSpent;
    }

    public void SetReloading(bool reloading, float progress)
    {
        if (reloadGroup != null && reloadGroup.activeSelf != reloading) reloadGroup.SetActive(reloading);
        if (reloading) ammoActivity = Time.time;
        if (reloadFill != null)
        {
            Vector2 max = reloadFill.anchorMax;
            max.x = Mathf.Clamp01(progress);
            reloadFill.anchorMax = max;
        }
    }

    public void SetEmpty(bool isEmpty)
    {
        empty = isEmpty;
        if (reloadPrompt != null && reloadPrompt.gameObject.activeSelf != isEmpty)
            reloadPrompt.gameObject.SetActive(isEmpty);
    }

    // spread and fov in degrees
    public void SetCrosshair(float spread, float fov, bool aiming, bool visible)
    {
        if (crosshair == null) return;

        if (crosshairGroup != null) crosshairGroup.alpha = visible ? (aiming ? aimFade : 1f) : 0f;
        aimingNow = aiming;
        if (aiming) ammoActivity = Time.time;

        // The gap grows with spread, so the crosshair shows how accurate you are
        Canvas canvas = crosshair.GetComponentInParent<Canvas>();
        float height = canvas != null ? ((RectTransform)canvas.transform).rect.height : 1080f;
        float gap = crosshairGap + spread * height / Mathf.Max(1f, fov);

        Place(crossTop, Vector2.up, gap);
        Place(crossBottom, Vector2.down, gap);
        Place(crossLeft, Vector2.left, gap);
        Place(crossRight, Vector2.right, gap);
    }

    public void SetHealth(float health, float max)
    {
        float k = Mathf.Clamp01(health / Mathf.Max(1f, max));
        if (Mathf.Abs(k - healthTarget) > 0.0001f) healthActivity = Time.time;
        healthTarget = k;
        if (healthTarget > healthShown) lagShown = healthTarget; // healing: no melting trail
    }

    // She hit you: red flash and the bar shakes
    public void FlashDamage()
    {
        damageFlash = 1f;
        shake = 1f;
        healthActivity = Time.time;
    }

    // Lighting a candle hurt you: warm glow
    public void FlashMemory()
    {
        memoryFlash = 1f;
        healthActivity = Time.time;
    }

    public void SetStamina(float stamina, float max)
    {
        staminaTarget = Mathf.Clamp01(stamina / Mathf.Max(1f, max));
    }

    // Tried to dodge without enough stamina: the bar flashes red and shakes
    public void NoStamina()
    {
        noStaminaFlash = 1f;
    }

    void UpdateHealthBar()
    {
        float dt = Time.deltaTime;

        // The bar drops quickly; the trail behind it slides down slowly
        healthShown = Mathf.MoveTowards(healthShown, healthTarget, dt * 1.5f);
        if (lagShown > healthShown) lagShown = Mathf.MoveTowards(lagShown, healthShown, dt * 0.3f);
        else lagShown = healthShown;

        if (healthFill != null)
        {
            SetWidth(healthFill.rectTransform, healthShown);
            healthFill.color = healthShown < 0.3f ? lowHealthColor : healthColor;
        }
        SetWidth(healthLag, lagShown);

        // Shake
        shake = Mathf.Max(0f, shake - dt * 3f);
        if (healthBar != null)
            healthBar.anchoredPosition = barHome + Random.insideUnitCircle * 6f * shake;

        // Stamina: shows while you're using it, fades away once it's full again
        staminaShown = Mathf.MoveTowards(staminaShown, staminaTarget, dt * 3f);
        noStaminaFlash = Mathf.Max(0f, noStaminaFlash - dt * 2f);
        bool show = staminaShown < 0.999f || noStaminaFlash > 0f;
        staminaVisible = Mathf.MoveTowards(staminaVisible, show ? 1f : 0f, dt * (show ? 6f : 1.5f));
        if (staminaBar != null)
        {
            staminaBar.alpha = staminaVisible;
            ((RectTransform)staminaBar.transform).anchoredPosition = staminaHome + Random.insideUnitCircle * 4f * noStaminaFlash;
        }
        if (staminaFill != null)
        {
            SetWidth(staminaFill.rectTransform, staminaShown);
            staminaFill.color = Color.Lerp(staminaColor, noStaminaColor, noStaminaFlash);
        }
    }

    void SetWidth(RectTransform r, float k)
    {
        if (r == null) return;
        Vector2 max = r.anchorMax;
        max.x = Mathf.Clamp01(k);
        r.anchorMax = max;
    }

    public void ShowHitMarker()
    {
        hitTimer = hitMarkerTime;
    }

    public void ShowEnding(string title, string line, bool showRestart)
    {
        ShowEnding(title, line, showRestart, false);
    }

    // overScene = true: the words appear over the game (a soft dark veil) instead of a black screen
    public void ShowEnding(string title, string line, bool showRestart, bool overScene)
    {
        if (endingPanel != null)
        {
            Image bg = endingPanel.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0f, 0f, 0f, overScene ? 0.35f : 1f);
        }

        if (endingTitle != null) endingTitle.text = title;
        if (endingLine != null) endingLine.text = line;
        if (restartHint != null) restartHint.gameObject.SetActive(showRestart);

        SetEmpty(false);
        SetReloading(false, 0f);

        if (endingPanel != null)
        {
            endingPanel.gameObject.SetActive(true);
            endingFade = 0f;
        }
    }

    // ---------- Helpers ----------

    // The two circles (faint full one behind, filling one in front), above the prompt
    Image promptBack, itemBack;

    // Bigger, brighter hint and memory text, a bigger circle right above the hint,
    // and a soft dark glow behind them so they read over anything
    void StrengthenPrompts()
    {
        if (promptLabel != null)
        {
            promptLabel.fontSize = 20f;
            promptLabel.fontStyle = FontStyles.Normal;
            promptLabel.characterSpacing = 8f;
            RectTransform pr = promptLabel.rectTransform;
            pr.sizeDelta = new Vector2(Mathf.Max(pr.sizeDelta.x, 520f), 64f); // room for two lines
            if (!soulsStyle) promptBack = SoftBack("Prompt Glow", pr, new Vector2(560f, 110f));

            foreach (Image ring in new[] { holdRingBack, holdRing })
            {
                if (ring == null) continue;
                RectTransform rr = ring.rectTransform;
                rr.anchorMin = pr.anchorMin;
                rr.anchorMax = pr.anchorMax;
                rr.anchoredPosition = pr.anchoredPosition + new Vector2(0f, 48f);
                rr.sizeDelta = new Vector2(52f, 52f);
            }
        }

        if (itemLabel != null)
        {
            itemLabel.fontSize = 21f;
            itemLabel.rectTransform.sizeDelta = new Vector2(900f, 70f);
            if (!soulsStyle) itemBack = SoftBack("Memory Glow", itemLabel.rectTransform, new Vector2(820f, 110f));
        }
    }

    // ---------- Dark Souls style ----------

    CanvasGroup promptBanner, itemBanner;
    static readonly Color SoulsLine = new Color(0.78f, 0.68f, 0.48f, 0.65f);   // old gold
    static readonly Color SoulsText = new Color(0.9f, 0.87f, 0.8f, 1f);        // parchment white

    void ApplySoulsStyle()
    {
        // Health: a framed deep red bar at the top left; stamina: a thinner muted green bar under it
        healthColor = new Color(0.58f, 0.09f, 0.07f, 0.95f);
        lowHealthColor = new Color(0.75f, 0.1f, 0.06f, 1f);
        staminaColor = new Color(0.32f, 0.47f, 0.3f, 0.95f);

        if (healthBar != null)
        {
            Place(healthBar, new Vector2(0f, 1f), new Vector2(50f, -50f), new Vector2(340f, 10f));
            Image bg = healthBar.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0.04f, 0.02f, 0.02f, 0.75f);
            if (healthLag != null)
            {
                Image lag = healthLag.GetComponent<Image>();
                if (lag != null) lag.color = new Color(0.85f, 0.7f, 0.35f, 0.75f);   // the gold "just lost" trail
            }
            Frame(healthBar, SoulsLine, 1.5f);
        }
        if (staminaBar != null)
        {
            RectTransform st = (RectTransform)staminaBar.transform;
            Place(st, new Vector2(0f, 1f), new Vector2(50f, -68f), new Vector2(260f, 6f));
            Image bg = st.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0.03f, 0.04f, 0.03f, 0.75f);
            Frame(st, SoulsLine, 1f);
        }

        // Counters under the bars
        if (candleCounter != null) { Place(candleCounter.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -86f), new Vector2(320f, 22f)); SoulsFont(candleCounter, 16f); }
        if (mementoCounter != null) { Place(mementoCounter.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -108f), new Vector2(320f, 22f)); SoulsFont(mementoCounter, 16f); }

        // Prompt and memory text: lower middle of the screen, upright parchment text
        if (promptLabel != null)
        {
            Place(promptLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(620f, 64f));
            promptLabel.alignment = TextAlignmentOptions.Center;
        }
        if (itemLabel != null)
        {
            Place(itemLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 270f), new Vector2(900f, 70f));
            itemLabel.alignment = TextAlignmentOptions.Center;
        }

        // Endings: huge, wide-spaced letters on a dark band across the middle of the screen
        if (endingTitle != null)
        {
            endingTitle.characterSpacing = 25f;
            endingTitle.fontStyle = FontStyles.Normal;
            endingTitle.color = SoulsText;
        }
        if (endingPanel != null && endingTitle != null)
        {
            RectTransform band = new GameObject("Ending Band", typeof(RectTransform)).GetComponent<RectTransform>();
            band.SetParent(endingPanel.transform, false);
            band.SetSiblingIndex(0);
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.anchoredPosition = new Vector2(0f, 30f);
            band.sizeDelta = new Vector2(0f, 300f);
            Image img = band.gameObject.AddComponent<Image>();
            img.sprite = FadeSprite(false);
            img.color = new Color(0f, 0f, 0f, 0.85f);
            img.raycastTarget = false;
        }
    }

    // Dark band with thin gold lines above and below, fading out at both ends (built at start)
    void BuildSoulsBanners()
    {
        if (promptLabel != null) promptBanner = MakeBanner("Prompt Banner", promptLabel.rectTransform);
        if (itemLabel != null) itemBanner = MakeBanner("Memory Banner", itemLabel.rectTransform);
        if (promptLabel != null) SoulsFont(promptLabel, 21f);
        if (itemLabel != null) SoulsFont(itemLabel, 22f);
    }

    CanvasGroup MakeBanner(string label, RectTransform text)
    {
        RectTransform root = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(text.parent, false);
        root.SetSiblingIndex(text.GetSiblingIndex()); // drawn just before the text
        root.anchorMin = root.anchorMax = text.anchorMin;
        root.pivot = new Vector2(0.5f, 0.5f);
        CanvasGroup g = root.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        g.blocksRaycasts = false;

        BannerPart("Band", root, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.78f));
        BannerPart("Top Line", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(0f, 1f), SoulsLine);
        BannerPart("Bottom Line", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f), SoulsLine);
        return g;
    }

    void BannerPart(string label, RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color color)
    {
        RectTransform rt = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = FadeSprite(true);
        img.color = color;
        img.raycastTarget = false;
    }

    // Size the banner to the text it sits behind
    void FitBanner(CanvasGroup banner, TMP_Text text, float height)
    {
        if (banner == null || text == null) return;
        RectTransform b = (RectTransform)banner.transform;
        RectTransform t = text.rectTransform;
        float width = Mathf.Max(text.GetPreferredValues(text.text).x, 200f) + 260f;
        b.anchorMin = b.anchorMax = t.anchorMin;
        b.anchoredPosition = t.anchoredPosition + new Vector2((0.5f - t.pivot.x) * t.sizeDelta.x, (0.5f - t.pivot.y) * t.sizeDelta.y);
        b.sizeDelta = new Vector2(width, height);
    }

    void SoulsFont(TMP_Text t, float size)
    {
        t.fontSize = size;
        t.fontStyle = FontStyles.Normal;
        t.characterSpacing = 4f;
        Color c = SoulsText;
        c.a = t.color.a;
        t.color = c;
    }

    void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = anchor;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    // Thin lines around the edges of a bar
    void Frame(RectTransform r, Color color, float thickness)
    {
        FrameLine(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), new Vector2(thickness, thickness), color);
        FrameLine(r, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(-thickness, -thickness), new Vector2(thickness, 0f), color);
        FrameLine(r, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-thickness, 0f), new Vector2(0f, 0f), color);
        FrameLine(r, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(thickness, 0f), color);
    }

    void FrameLine(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color color)
    {
        RectTransform rt = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    // White strip that fades out at both ends (sideways = left/right, otherwise top/bottom)
    static Sprite fadeSideways, fadeUpDown;
    static Sprite FadeSprite(bool sideways)
    {
        if (sideways && fadeSideways != null) return fadeSideways;
        if (!sideways && fadeUpDown != null) return fadeUpDown;
        int w = sideways ? 128 : 4, h = sideways ? 4 : 128;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = sideways ? (x + 0.5f) / w : (y + 0.5f) / h;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(k, 1f - k) / 0.3f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        if (sideways) fadeSideways = sp; else fadeUpDown = sp;
        return sp;
    }

    // A soft, blurry dark patch placed behind a piece of text
    Image SoftBack(string label, RectTransform behind, Vector2 size)
    {
        GameObject go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(behind.parent, false);
        go.transform.SetSiblingIndex(behind.GetSiblingIndex()); // drawn just before the text
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = behind.anchorMin;
        rt.anchorMax = behind.anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = behind.anchoredPosition + new Vector2((0.5f - behind.pivot.x) * behind.sizeDelta.x, (0.5f - behind.pivot.y) * behind.sizeDelta.y);
        rt.sizeDelta = size;
        Image img = go.AddComponent<Image>();
        img.sprite = SoftBlob();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        return img;
    }

    static Sprite blobSprite;
    static Sprite SoftBlob()
    {
        if (blobSprite != null) return blobSprite;
        int n = 64;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        blobSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return blobSprite;
    }

    void MakeRings(Transform parent)
    {
        holdRingBack = MakeRing("Hold Ring Back", parent);
        holdRing = MakeRing("Hold Ring", parent);
    }

    Image MakeRing(string label, Transform parent)
    {
        GameObject go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 145f);
        rt.sizeDelta = new Vector2(34f, 34f);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.86f, 0.83f, 0.78f, 0f);
        img.raycastTarget = false;
        return img;
    }

    // A thin soft ring, made in code
    static Sprite ringSprite;
    static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        int n = 128;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.85f) / 0.07f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return ringSprite;
    }

    // Puts a crosshair line "gap" away from the center, in the given direction
    void Place(RectTransform line, Vector2 direction, float gap)
    {
        if (line == null) return;
        float half = direction.y != 0f ? line.rect.height * 0.5f : line.rect.width * 0.5f;
        line.anchoredPosition = direction * (gap + half);
    }

    void BuildBullets(int count)
    {
        if (bulletRow == null || bulletTemplate == null) return;

        foreach (Image b in bullets) if (b != null) Destroy(b.gameObject);
        bullets.Clear();

        for (int i = 0; i < count; i++)
        {
            Image b = Instantiate(bulletTemplate, bulletRow);
            b.gameObject.SetActive(true);
            b.name = "Bullet " + (i + 1);
            bullets.Add(b);
        }
    }

    // ---------- Builds a ready-to-use Canvas in your scene (you can restyle it after) ----------

#if UNITY_EDITOR
    [ContextMenu("Build Default UI")]
    void BuildDefaultUI()
    {
        GameObject canvasGo = new GameObject("Game UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Build Default UI");

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        // HUD
        RectTransform hudRt = Panel("HUD", canvasGo.transform);
        hud = hudRt.gameObject.AddComponent<CanvasGroup>();
        hud.blocksRaycasts = false;

        // Crosshair
        crosshair = Box("Crosshair", hudRt, Vector2.one * 0.5f, Vector2.zero, new Vector2(60f, 60f), Color.clear);
        Color bone = new Color(0.86f, 0.83f, 0.78f, 0.7f);
        crossTop = Box("Top", crosshair, Vector2.one * 0.5f, Vector2.zero, new Vector2(1.5f, 6f), bone);
        crossBottom = Box("Bottom", crosshair, Vector2.one * 0.5f, Vector2.zero, new Vector2(1.5f, 6f), bone);
        crossLeft = Box("Left", crosshair, Vector2.one * 0.5f, Vector2.zero, new Vector2(6f, 1.5f), bone);
        crossRight = Box("Right", crosshair, Vector2.one * 0.5f, Vector2.zero, new Vector2(6f, 1.5f), bone);
        Box("Dot", crosshair, Vector2.one * 0.5f, Vector2.zero, new Vector2(2f, 2f), new Color(0.86f, 0.83f, 0.78f, 0.5f));

        // Hit marker (an X)
        RectTransform hm = Box("Hit Marker", hudRt, Vector2.one * 0.5f, Vector2.zero, new Vector2(60f, 60f), Color.clear);
        hm.localRotation = Quaternion.Euler(0f, 0f, 45f);
        hitMarker = hm.gameObject.AddComponent<CanvasGroup>();
        Color hit = new Color(0.75f, 0.25f, 0.22f, 0.9f);
        Box("Up", hm, Vector2.one * 0.5f, new Vector2(0f, 9f), new Vector2(1.5f, 7f), hit);
        Box("Down", hm, Vector2.one * 0.5f, new Vector2(0f, -9f), new Vector2(1.5f, 7f), hit);
        Box("Left", hm, Vector2.one * 0.5f, new Vector2(-9f, 0f), new Vector2(7f, 1.5f), hit);
        Box("Right", hm, Vector2.one * 0.5f, new Vector2(9f, 0f), new Vector2(7f, 1.5f), hit);

        // Ammo, bottom right
        RectTransform ammo = Box("Ammo", hudRt, new Vector2(1f, 0f), new Vector2(-60f, 56f), new Vector2(240f, 70f), Color.clear);
        ammo.pivot = new Vector2(1f, 0f);
        ammoGroup = ammo.gameObject.AddComponent<CanvasGroup>();
        ammoMaxText = Text("Max", ammo, "/ 12", 16, FontStyles.Normal, TextAlignmentOptions.BottomRight,
                           new Vector2(1f, 0f), new Vector2(0f, 18f), new Vector2(50f, 24f), new Color(0.86f, 0.83f, 0.78f, 0.4f));
        ammoText = Text("Count", ammo, "12", 38, FontStyles.Normal, TextAlignmentOptions.BottomRight,
                        new Vector2(1f, 0f), new Vector2(-54f, 14f), new Vector2(120f, 48f), ammoNormal);
        bulletRow = Box("Bullets", ammo, new Vector2(1f, 0f), Vector2.zero, new Vector2(200f, 9f), Color.clear);
        bulletRow.pivot = new Vector2(1f, 0f);
        HorizontalLayoutGroup row = bulletRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.LowerRight;
        row.spacing = 2f;
        row.childControlWidth = false;
        row.childControlHeight = false;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        bulletTemplate = Box("Bullet Template", bulletRow, Vector2.one * 0.5f, Vector2.zero, new Vector2(3f, 9f), Color.white).GetComponent<Image>();
        bulletTemplate.gameObject.SetActive(false);

        // Reload bar under the crosshair
        RectTransform reload = Box("Reload", hudRt, Vector2.one * 0.5f, new Vector2(0f, -40f), new Vector2(80f, 10f), Color.clear);
        reloadGroup = reload.gameObject;
        RectTransform barBg = Box("Bar Background", reload, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(80f, 2f), new Color(0.86f, 0.83f, 0.78f, 0.15f));
        reloadFill = Box("Bar Fill", barBg, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0.86f, 0.83f, 0.78f, 0.7f));
        reloadFill.anchorMin = Vector2.zero;
        reloadFill.anchorMax = new Vector2(0f, 1f);
        reloadFill.offsetMin = Vector2.zero;
        reloadFill.offsetMax = Vector2.zero;

        reloadPrompt = Text("Reload Prompt", hudRt, "reload", 14, FontStyles.Italic, TextAlignmentOptions.Center,
                            Vector2.one * 0.5f, new Vector2(0f, -38f), new Vector2(200f, 22f), new Color(0.7f, 0.25f, 0.22f, 0.8f));
        reloadPrompt.characterSpacing = 8f;

        // Ending screen
        // Health bar, bottom left
        healthBar = Box("Health Bar", hudRt, Vector2.zero, new Vector2(60f, 60f), new Vector2(200f, 3f), new Color(0f, 0f, 0f, 0.35f));
        healthBar.pivot = Vector2.zero;
        healthGroup = healthBar.gameObject.AddComponent<CanvasGroup>();
        healthLag = Stretch("Lost", healthBar, new Color(0.6f, 0.15f, 0.12f, 0.5f));
        healthFill = Stretch("Fill", healthBar, healthColor).GetComponent<Image>();

        // Stamina bar, just under the health bar (hidden while full)
        RectTransform stam = Box("Stamina Bar", hudRt, Vector2.zero, new Vector2(60f, 52f), new Vector2(200f, 2f), new Color(0f, 0f, 0f, 0.3f));
        stam.pivot = Vector2.zero;
        staminaBar = stam.gameObject.AddComponent<CanvasGroup>();
        staminaFill = Stretch("Fill", stam, staminaColor).GetComponent<Image>();

        // Counters, top left
        candleCounter = Text("Candle Counter", hudRt, "candles   0 / 0", 15, FontStyles.Italic, TextAlignmentOptions.TopLeft,
                             new Vector2(0f, 1f), new Vector2(60f, -50f), new Vector2(300f, 22f), new Color(0.86f, 0.83f, 0.78f, 0.35f));
        candleCounter.characterSpacing = 6f;
        mementoCounter = Text("Memento Counter", hudRt, "", 15, FontStyles.Italic, TextAlignmentOptions.TopLeft,
                              new Vector2(0f, 1f), new Vector2(60f, -74f), new Vector2(300f, 22f), new Color(0.86f, 0.83f, 0.78f, 0.35f));
        mementoCounter.characterSpacing = 6f;

        // Circle that fills while holding E, just above the hint
        MakeRings(hudRt);

        // Hint, a little above the item name
        promptLabel = Text("Prompt", hudRt, "", 14, FontStyles.Italic, TextAlignmentOptions.Center,
                           new Vector2(0.5f, 0f), new Vector2(0f, 105f), new Vector2(400f, 22f), new Color(0.86f, 0.83f, 0.78f, 0f));
        promptLabel.characterSpacing = 10f;

        // Name of the item you switched to, bottom center
        itemLabel = Text("Item Name", hudRt, "", 15, FontStyles.Italic, TextAlignmentOptions.Center,
                         new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(400f, 24f), new Color(0.86f, 0.83f, 0.78f, 0f));
        itemLabel.characterSpacing = 6f;

        // Full-screen flashes (under the ending screen)
        damageOverlay = Panel("Damage Flash", canvasGo.transform).gameObject.AddComponent<Image>();
        damageOverlay.color = new Color(damageColor.r, damageColor.g, damageColor.b, 0f);
        damageOverlay.raycastTarget = false;
        memoryOverlay = Panel("Memory Flash", canvasGo.transform).gameObject.AddComponent<Image>();
        memoryOverlay.color = new Color(memoryColor.r, memoryColor.g, memoryColor.b, 0f);
        memoryOverlay.raycastTarget = false;

        RectTransform end = Panel("Ending Screen", canvasGo.transform);
        end.gameObject.AddComponent<Image>().color = Color.black;
        endingPanel = end.gameObject.AddComponent<CanvasGroup>();
        endingTitle = Text("Title", end, "FORGOTTEN", 110, FontStyles.Bold, TextAlignmentOptions.Center,
                           Vector2.one * 0.5f, new Vector2(0f, 80f), new Vector2(1600f, 160f), new Color(0.85f, 0.85f, 0.9f));
        endingTitle.characterSpacing = 12f;
        endingTitle.enableAutoSizing = true; // long titles shrink to fit
        endingTitle.fontSizeMin = 40f;
        endingTitle.fontSizeMax = 90f;
        endingLine = Text("Line", end, "No one remembers you now. Not even you.", 34, FontStyles.Italic, TextAlignmentOptions.Center,
                          Vector2.one * 0.5f, new Vector2(0f, -40f), new Vector2(1400f, 60f), new Color(0.6f, 0.6f, 0.65f));
        restartHint = Text("Restart Hint", end, "Press R to try again", 22, FontStyles.Normal, TextAlignmentOptions.Center,
                           new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(600f, 40f), new Color(0.4f, 0.4f, 0.45f));
        end.gameObject.SetActive(false);

        EditorUtility.SetDirty(this);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Selection.activeGameObject = canvasGo;
        Debug.Log("UIManager: built 'Game UI'. Restyle anything you like; just keep the slots on UIManager filled.");
    }

    RectTransform Panel(string label, Transform parent)
    {
        GameObject go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    RectTransform Box(string label, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        if (color.a > 0f)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }
        return rt;
    }

    TMP_Text Text(string label, Transform parent, string text, float size, FontStyles style, TextAlignmentOptions align,
                  Vector2 anchor, Vector2 pos, Vector2 boxSize, Color color)
    {
        GameObject go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = boxSize;

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    RectTransform Stretch(string label, Transform parent, Color color)
    {
        RectTransform rt = Box(label, parent, Vector2.zero, Vector2.zero, Vector2.zero, color);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    void AddOutline(RectTransform r)
    {
        Outline o = r.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.5f);
        o.effectDistance = new Vector2(1f, -1f);
    }
#endif
}