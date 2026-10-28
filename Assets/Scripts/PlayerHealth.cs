using UnityEngine;
using UnityEngine.SceneManagement;

// The player's health.
// She hurts you a lot (red flash). Lighting a candle hurts a little (warm flash):
// remembering costs something, but it can never finish you off.
// Health slowly comes back when nothing has hurt you for a while.
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance;

    public float maxHealth = 100f;
    [Tooltip("Damage when she catches you.")]
    public float ghostDamage = 30f;
    [Tooltip("Damage when you light a candle. It can't take your last bit of health.")]
    public float candleDamage = 10f;
    [Tooltip("Health back per second, once nothing has hurt you for a few seconds. 0 = never.")]
    public float regenPerSecond = 2f;

    const float SafeTimeAfterHit = 2f;  // she can't hurt you again right away
    const float RegenDelay = 6f;        // seconds without damage before healing starts

    public float Health { get; private set; }

    float safeTimer;
    float sinceDamage;
    float redFlash, warmFlash;

    void Awake() { Instance = this; }
    void OnEnable() { Candle.AnyLit += OnCandleLit; }
    void OnDisable() { Candle.AnyLit -= OnCandleLit; }

    void Start()
    {
        Debug.Log("PlayerHealth: ready on '" + name + "' with " + maxHealth + " health.");
        Health = maxHealth;
        UpdateUI();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        safeTimer -= dt;
        sinceDamage += dt;
        redFlash = Mathf.Max(0f, redFlash - dt * 2f);
        warmFlash = Mathf.Max(0f, warmFlash - dt * 0.8f);

        if (Health > 0f && Health < maxHealth && regenPerSecond > 0f && sinceDamage > RegenDelay)
        {
            Health = Mathf.Min(maxHealth, Health + regenPerSecond * dt);
            UpdateUI();
        }
    }

    // Called by the ghost. Returns false if you were still safe from the last hit.
    public bool TakeHit()
    {
        if (Health <= 0f || safeTimer > 0f) return false;

        // Slipping past her at the start of a sprint: no damage
        if (PlayerMovement.Instance != null && PlayerMovement.Instance.IsSafe) return false;

        Health = Mathf.Max(0f, Health - ghostDamage);
        safeTimer = SafeTimeAfterHit;
        sinceDamage = 0f;
        redFlash = 1f;
        Debug.Log("PlayerHealth: she caught you. Health " + Mathf.RoundToInt(Health) + " / " + maxHealth);
        UpdateUI();
        if (UIManager.Instance != null) UIManager.Instance.FlashDamage();
        if (SoundManager.Instance != null) SoundManager.Instance.PlayerHurt();

        if (Health <= 0f)
        {
            if (EndingManager.Instance != null) EndingManager.Instance.PlayerFell();
            else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        return true;
    }

    // Lighting a candle hurts a little, but never takes your last bit of health
    void OnCandleLit(Candle c)
    {
        if (Time.timeSinceLevelLoad < 0.5f || Health <= 0f) return; // candles lit at the start don't count

        Health = Mathf.Max(Mathf.Min(Health, 1f), Health - candleDamage);
        sinceDamage = 0f;
        warmFlash = 1f;
        Debug.Log("PlayerHealth: lighting the candle hurt. Health " + Mathf.RoundToInt(Health) + " / " + maxHealth);
        UpdateUI();
        if (UIManager.Instance != null) UIManager.Instance.FlashMemory();
        if (SoundManager.Instance != null) SoundManager.Instance.CandlePain();
    }

    // Called by the tree while you're near it
    // Returns true if you actually healed (you weren't already full)
    public bool Heal(float amount)
    {
        if (Health <= 0f || Health >= maxHealth) return false;
        Health = Mathf.Min(maxHealth, Health + amount);
        UpdateUI();
        return true;
    }

    void UpdateUI()
    {
        if (UIManager.Instance != null) UIManager.Instance.SetHealth(Health, maxHealth);
    }

    // Backup visuals when there's no UIManager
    void OnGUI()
    {
        if (UIManager.Instance != null || Event.current.type != EventType.Repaint) return;

        if (redFlash > 0f)
        {
            GUI.color = new Color(0.5f, 0f, 0f, redFlash * 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        if (warmFlash > 0f)
        {
            GUI.color = new Color(1f, 0.8f, 0.5f, warmFlash * 0.3f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        // Simple bar, bottom left
        float k = Health / Mathf.Max(1f, maxHealth);
        GUI.color = new Color(0f, 0f, 0f, 0.4f);
        GUI.DrawTexture(new Rect(40, Screen.height - 200, 12, 160), Texture2D.whiteTexture);
        GUI.color = new Color(0.92f, 0.88f, 0.8f, 0.9f);
        GUI.DrawTexture(new Rect(40, Screen.height - 40 - 160 * k, 12, 160 * k), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}