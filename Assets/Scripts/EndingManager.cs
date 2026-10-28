using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Watches for the endings and plays them.
// Ending 1, FORGOTTEN: she swallows the candles until none are left burning.
// Ending 2, EMPTY: you kill her.
// (The best ending will be added here later.)
public class EndingManager : MonoBehaviour
{
    public static EndingManager Instance;

    [Header("Ending: Forgotten (she swallows the candles)")]
    [Tooltip("How many candles she has to swallow in total before this ending can happen.")]
    public int candlesSwallowedToLose = 5;
    [Tooltip("After the last candle goes out, you have this many seconds to relight one and save yourself.")]
    public float lastChanceTime = 5f;
    public string forgottenTitle = "NO ONE LEFT TO KEEP THE LIGHT ON";
    [TextArea(2, 4)]
    public string forgottenLine = "The wax grew cold so softly.\nYou never heard it go.";

    [Header("Ending: Empty (you kill her)")]
    [Tooltip("Seconds after she dies before the ending screen, so you see her fade.")]
    public float waitAfterKill = 3f;
    public string emptyTitle = "THE QUIET YOU ASKED FOR";
    [TextArea(2, 4)]
    public string emptyLine = "It only ached because it held something.\nNow it holds nothing at all.";

    [Header("Ending: Everything Was Lit (you shoot her in the final moment)")]
    public string litTitle = "EVERYTHING WAS LIT";
    [TextArea(2, 4)]
    public string litLine = "It came to you with open hands.\nYou only saw it coming.";

    [Header("Ending: Caught (your health runs out)")]
    public string caughtTitle = "YOU LET IT HOLD YOU";
    [TextArea(2, 4)]
    public string caughtLine = "It was warm, at first.\nThen it was everything.";

    [Header("Ending: Remembered (the best ending)")]
    [Tooltip("Every memory found and every candle lit: she vanishes, then lunges from right in front of you. Don't shoot, then join her at the tree.")]
    public string rememberedTitle = "THE LIGHT WAS ALWAYS YOURS";
    [TextArea(2, 4)]
    public string rememberedLine = "It was only heavy because it was full.\nYou carried it anyway.";
    [Tooltip("Shown once, when the last memory is found.")]
    [TextArea(2, 3)] public string allFoundHint = "Every light must burn.\nThen let her come.";
    [Tooltip("Seconds of looking at the blooming tree before the words appear.")]
    public float rememberedTextDelay = 6f;

    [Header("Testing")]
    [Tooltip("While playing: F1-F6 jump straight to each ending (see the Console when the game starts). Untick before you build the game.")]
    public bool testKeys = true;

    [Header("Ending Screen")]
    public bool pressRToRestart = true;

    [Header("Player")]
    [Tooltip("Player scripts to switch off when an ending starts. Leave empty to find PlayerMovement, MouseLook and PlayerGun automatically.")]
    public MonoBehaviour[] playerScripts;

    int swallowed;
    bool ended;
    bool lastChance;
    float fade;
    bool showText;
    string title;
    string line;

    bool finalRunning;
    bool hintShown;
    float nextStatus;

    void Start()
    {
        if (testKeys)
            Debug.Log("EndingManager TEST KEYS:  F1 = candles all out   F2 = killed her   F3 = your health ran out   "
                      + "F4 = shot her at the end   F5 = best ending (text only)   F6 = start the real final moment   (R restarts after an ending)");
    }

    void Update()
    {
        if (showText && pressRToRestart && Input.GetKeyDown(KeyCode.R))
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        if (testKeys && !ended)
        {
            if (Input.GetKeyDown(KeyCode.F1)) TestForgotten();
            if (Input.GetKeyDown(KeyCode.F2)) TestKilled();
            if (Input.GetKeyDown(KeyCode.F3)) TestCaught();
            if (Input.GetKeyDown(KeyCode.F4)) TestShotAtTheEnd();
            if (Input.GetKeyDown(KeyCode.F5)) TestBest();
            if (Input.GetKeyDown(KeyCode.F6)) TestFinalMoment();
        }

        // Every memory found: one quiet hint about what's left
        if (!ended && !hintShown && Memento.AllCollected)
        {
            hintShown = true;
            if (UIManager.Instance != null) UIManager.Instance.ShowMemory(allFoundHint);
        }

        // The final moment starts when every memory is found and every candle is lit
        if (!ended && !finalRunning && Memento.AllCollected)
        {
            ShadowPerson ghost = FindObjectOfType<ShadowPerson>();
            bool allLit = BloomTree.Instance != null && BloomTree.Instance.FullBloom;

            if (allLit && ghost != null && ghost.StartFinalTest())
            {
                finalRunning = true;
                Debug.Log("EndingManager: the final moment begins.");
            }
            else if (Time.time > nextStatus)
            {
                // Every few seconds, say exactly what's still missing
                nextStatus = Time.time + 5f;
                string unlit = "";
                int lit = 0, total = 0;
                foreach (Candle c in Candle.All)
                {
                    if (c == null) continue;
                    total++;
                    if (c.IsLit) lit++; else unlit += (unlit.Length > 0 ? ", " : "") + c.name;
                }
                Debug.Log("EndingManager: all memories found. Waiting for the final moment. Candles lit " + lit + " / " + total
                          + (unlit.Length > 0 ? " (unlit: " + unlit + ")" : "")
                          + (BloomTree.Instance == null ? ". No BloomTree in the scene!" : "")
                          + (ghost == null ? ". No ghost found!" : "")
                          + (ghost != null && allLit ? ". The ghost couldn't start it (is she dead, or is the player missing?)" : ""));
            }
        }
    }

    // She reached the tree: the best ending
    public void FinalReached()
    {
        if (ended) return;
        ended = true;
        if (SoundManager.Instance != null) SoundManager.Instance.EndingSound(true);
        StartCoroutine(RememberedEnding());
    }

    IEnumerator RememberedEnding()
    {
        // You can walk and look around freely to watch her merge with the tree.
        // The gun is lowered (empty hands) and can't be taken out again.
        WeaponSwitcher ws = FindObjectOfType<WeaponSwitcher>();
        if (ws != null) { ws.Select(0, false); ws.enabled = false; }
        yield return new WaitForSeconds(rememberedTextDelay);

        title = rememberedTitle;
        line = rememberedLine;
        showText = true;
        if (UIManager.Instance != null) UIManager.Instance.ShowEnding(title, line, pressRToRestart, true);
        else fade = 0.35f; // backup screen: a soft veil over the scene
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("There are two EndingManagers in the scene. Removing this one (on '" + name + "').", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }
    void OnEnable() { Candle.AnyBlownOut += OnCandleBlownOut; }
    void OnDisable() { Candle.AnyBlownOut -= OnCandleBlownOut; }

    void OnCandleBlownOut(Candle c)
    {
        if (ended) return;
        swallowed++;
        Debug.Log("EndingManager: she has swallowed " + swallowed + " / " + candlesSwallowedToLose + " candles.");

        if (swallowed >= candlesSwallowedToLose && NoCandlesLit() && !lastChance)
            StartCoroutine(ForgottenCountdown());
    }

    bool NoCandlesLit()
    {
        foreach (Candle c in Candle.All)
            if (c != null && c.IsLit) return false;
        return true;
    }

    IEnumerator ForgottenCountdown()
    {
        lastChance = true;
        yield return new WaitForSeconds(lastChanceTime);
        lastChance = false;

        // You relit a candle in time, or another ending already happened (like killing her)
        if (ended || !NoCandlesLit()) yield break;

        StartCoroutine(PlayEnding(forgottenTitle, forgottenLine));
    }

    // Called by PlayerHealth when your health runs out
    public void PlayerFell()
    {
        TriggerEnding(caughtTitle, caughtLine);
    }

    // Called by the ghost when she dies
    public void SheDied()
    {
        if (!ended) StartCoroutine(EmptyEnding());
    }

    // ---------- Testing (also in the component's three-dots menu while playing) ----------

    [ContextMenu("Test: Candles All Out")]
    void TestForgotten() { if (CanTest()) StartCoroutine(PlayEnding(forgottenTitle, forgottenLine)); }

    [ContextMenu("Test: Killed Her")]
    void TestKilled() { if (CanTest()) SheDied(); }

    [ContextMenu("Test: Health Ran Out")]
    void TestCaught() { if (CanTest()) PlayerFell(); }

    [ContextMenu("Test: Shot Her At The End")]
    void TestShotAtTheEnd() { if (CanTest()) SheDiedAtTheEnd(); }

    [ContextMenu("Test: Best Ending (text only)")]
    void TestBest()
    {
        if (!CanTest()) return;
        if (BloomTree.Instance != null) BloomTree.Instance.Blossom();
        FinalReached();
    }

    [ContextMenu("Test: Start The Real Final Moment")]
    void TestFinalMoment()
    {
        if (!CanTest()) return;
        ShadowPerson ghost = FindObjectOfType<ShadowPerson>();
        if (ghost != null && ghost.StartFinalTest())
        {
            finalRunning = true;
            Debug.Log("EndingManager TEST: final moment started. Don't shoot for the best ending, shoot for 'Everything Was Lit'.");
        }
        else Debug.LogWarning("EndingManager TEST: couldn't start it (no ghost, no BloomTree, or she's dead).");
    }

    bool CanTest()
    {
        if (!Application.isPlaying) { Debug.Log("EndingManager: press Play first, then test."); return false; }
        if (ended) { Debug.Log("EndingManager: an ending already happened. Press R to restart."); return false; }
        Debug.Log("EndingManager TEST: triggering an ending.");
        return true;
    }

    // Called by the ghost when you shoot her during the final moment
    public void SheDiedAtTheEnd()
    {
        if (!ended) StartCoroutine(EmptyEnding(litTitle, litLine));
    }

    IEnumerator EmptyEnding() { return EmptyEnding(emptyTitle, emptyLine); }

    IEnumerator EmptyEnding(string endingTitle, string endingLine)
    {
        ended = true; // nothing else can end the game now
        yield return new WaitForSeconds(waitAfterKill);
        StartCoroutine(PlayEnding(endingTitle, endingLine));
    }

    // Other scripts can call this later for the other endings
    public void TriggerEnding(string endingTitle, string endingLine)
    {
        if (!ended) StartCoroutine(PlayEnding(endingTitle, endingLine));
    }

    IEnumerator PlayEnding(string endingTitle, string endingLine)
    {
        // Only ever show one ending screen
        if (showText) yield break;

        ended = true;
        title = endingTitle;
        line = endingLine;

        FreezePlayer();
        if (SoundManager.Instance != null) SoundManager.Instance.EndingSound(false);

        // She stops moving
        ShadowPerson ghost = FindObjectOfType<ShadowPerson>();
        if (ghost != null) ghost.enabled = false;

        // Black screen with the ending text
        showText = true;
        if (UIManager.Instance != null) UIManager.Instance.ShowEnding(title, line, pressRToRestart);
        else fade = 1f; // backup screen drawn by this script
        yield break;
    }

    void FreezePlayer() { FreezePlayer(false); }

    void FreezePlayer(bool keepLooking)
    {
        if (playerScripts != null && playerScripts.Length > 0)
        {
            foreach (MonoBehaviour m in playerScripts)
                if (m != null) m.enabled = false;
            return;
        }

        foreach (MonoBehaviour m in FindObjectsOfType<MonoBehaviour>())
        {
            string n = m.GetType().Name;
            if (n == "MouseLook" && keepLooking) continue;
            if (n == "PlayerMovement" || n == "MouseLook" || n == "PlayerGun" || n == "Gun" || n == "PlayerDodge" || n == "WeaponSwitcher" || n == "PlayerJump") m.enabled = false;
        }
    }


    void OnGUI()
    {
        if (fade <= 0f) return;

        GUI.color = new Color(0f, 0f, 0f, fade);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        if (!showText) return;

        GUI.color = Color.white;

        GUIStyle big = new GUIStyle(GUI.skin.label);
        big.fontSize = Screen.height / 10;
        big.alignment = TextAnchor.MiddleCenter;
        big.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

        GUIStyle small = new GUIStyle(big);
        small.fontSize = Screen.height / 30;
        small.normal.textColor = new Color(0.6f, 0.6f, 0.65f);

        float h = Screen.height;
        GUI.Label(new Rect(0, h * 0.35f, Screen.width, h * 0.15f), title, big);
        GUI.Label(new Rect(0, h * 0.5f, Screen.width, h * 0.08f), line, small);

        if (pressRToRestart)
        {
            GUIStyle tiny = new GUIStyle(small);
            tiny.fontSize = Screen.height / 45;
            tiny.normal.textColor = new Color(0.4f, 0.4f, 0.45f);
            GUI.Label(new Rect(0, h * 0.8f, Screen.width, h * 0.05f), "Press R to try again", tiny);
        }
    }
}