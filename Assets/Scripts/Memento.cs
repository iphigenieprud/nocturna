using System.Collections.Generic;
using UnityEngine;

// A memory to find: not an object, just a soft glowing light (it can sit under the water).
// Put this on an EMPTY object. Walk close and press E: the light rises into you and its line appears.
// Find all of them, then go to the tomb tree: that starts the final moment.
public class Memento : MonoBehaviour
{
    public static readonly List<Memento> All = new List<Memento>();

    // How many there will be in total (set by MementoSpawner). 0 = just the ones in the scene.
    public static int ExpectedTotal;

    public static int Total { get { return Mathf.Max(ExpectedTotal, All.Count); } }

    // Every memento has been found (including ones that haven't appeared yet)
    public static bool AllCollected
    {
        get { return Total > 0 && CollectedCount >= Total; }
    }

    public static int CollectedCount
    {
        get { int n = 0; foreach (Memento m in All) if (m != null && m.Collected) n++; return n; }
    }

    [Tooltip("The line that appears when you take it. Short and cryptic works best.")]
    [TextArea(2, 3)] public string memoryLine = "Someone laughing, far away.";
    [Tooltip("Soft round particle material. GhostFogMat works.")]
    public Material glowMaterial;
    public Color glowColor = new Color(1f, 0.85f, 0.6f);
    [Tooltip("How close you need to be (ignores height, so it works under the water).")]
    public float pickupDistance = 2f;
    public KeyCode pickupKey = KeyCode.E;
    [Tooltip("What it is, shown on top when you're close.")]
    public string title = "a memory";
    [Tooltip("What to do, shown under it.")]
    public string prompt = "[E]   remember";
    public AudioClip pickupSound;

    public bool Collected { get; private set; }

    ParticleSystem core, halo, motes;
    Light glowLight;
    Vector3 home;
    float seed;

    // Backup text when the UI has no label for it
    static string shownLine;
    static float shownTimer;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDestroy() { All.Remove(this); }

    void Start()
    {
        home = transform.position;
        seed = Random.value * 10f;

        // Any model on this object is hidden: the memento is only light
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        GameObject g = new GameObject("Memento Light");
        g.transform.SetParent(transform, false);
        glowLight = g.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = glowColor;
        glowLight.range = 2.5f;
        glowLight.shadows = LightShadows.None;

        if (glowMaterial == null)
        {
            Debug.LogWarning("Memento: drag GhostFogMat into 'Glow Material' so the memory shows as light.", this);
            return;
        }

        // A bright soft core, a wide faint halo, and tiny motes drifting up like bubbles of light
        core = MakeParticles("Core", 3f, 0.9f, 4f, new Vector2(0.12f, 0.2f), new Vector2(1f, 1.5f), 0f, 0.02f);
        halo = MakeParticles("Halo", 0.8f, 0.35f, 2f, new Vector2(0.5f, 0.8f), new Vector2(2f, 3f), 0f, 0.05f);
        motes = MakeParticles("Motes", 2f, 0.9f, 3f, new Vector2(0.02f, 0.04f), new Vector2(2f, 3.5f), 0.15f, 0.15f);
        var vel = motes.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
    }

    void Update()
    {
        if (Collected) return;

        // Slow drift and a breathing glow
        transform.position = home + new Vector3(Mathf.Sin(Time.time * 0.7f + seed) * 0.04f,
                                                Mathf.Sin(Time.time * 1.1f + seed) * 0.05f, 0f);
        if (glowLight != null) glowLight.intensity = 0.8f + 0.4f * Mathf.Sin(Time.time * 2f + seed);

        // Hold E when close (with empty hands) to take the memory
        bool close = PlayerDistance() < pickupDistance;
        if (hold.Tick(this, close, title, prompt, pickupKey)) PickUp();
    }

    readonly HoldInteract hold = new HoldInteract();

    void PickUp()
    {
        Collected = true;
        hold.Clear(this);
        if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position, 0.8f);
        else if (SoundManager.Instance != null) SoundManager.Instance.MemoryTaken();
        if (SoundManager.Instance != null) SoundManager.Instance.MemoryMusic();   // the next part of the piano

        if (UIManager.Instance != null && UIManager.Instance.HasItemLabel) UIManager.Instance.ShowMemory(memoryLine);
        else { shownLine = memoryLine; shownTimer = 4f; }
        Debug.Log("Memento: " + CollectedCount + " / " + All.Count + " found.");

        // The light rises up into you, then it's gone
        if (core != null) core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (halo != null) halo.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (motes != null)
        {
            motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Vector3 target = Camera.main != null ? Camera.main.transform.position - Vector3.up * 0.3f : transform.position + Vector3.up * 2f;
            for (int i = 0; i < 40; i++)
            {
                var p = new ParticleSystem.EmitParams();
                p.position = transform.position + Random.insideUnitSphere * 0.15f;
                float life = Random.Range(0.8f, 1.4f);
                p.velocity = (target + Random.insideUnitSphere * 0.3f - p.position) / life;
                p.startLifetime = life;
                p.startSize = Random.Range(0.02f, 0.05f);
                p.applyShapeToPosition = false;
                motes.Emit(p, 1);
            }
        }
        if (glowLight != null) glowLight.enabled = false;
        Invoke(nameof(Hide), 2f);
    }

    void Hide() { gameObject.SetActive(false); }

    ParticleSystem MakeParticles(string label, float brightness, float alpha, float rate, Vector2 size, Vector2 life, float speed, float radius)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startSpeed = speed;
        main.startColor = Color.white;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 100;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.001f, radius);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.8f;

        // Glowing copy of the material (shines with Bloom), drawn after the water so it shows through it
        Material m = new Material(glowMaterial);
        Color c = glowColor * brightness;
        c.a = alpha;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        m.renderQueue = 3100;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = m;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        ps.Play();
        return ps;
    }

    float PlayerDistance()
    {
        Transform p = PlayerMovement.Instance != null ? PlayerMovement.Instance.transform
                    : Camera.main != null ? Camera.main.transform : null;
        if (p == null) return Mathf.Infinity;
        Vector3 a = p.position; a.y = 0f;
        Vector3 b = home; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Backup text, drawn by one of the mementos still in the scene
    void OnGUI()
    {

        if (shownTimer <= 0f || Event.current.type != EventType.Repaint) return;
        if (FirstActive() != this) return;
        shownTimer -= Time.deltaTime;
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.alignment = TextAnchor.MiddleCenter;
        st.fontStyle = FontStyle.Italic;
        st.fontSize = Mathf.RoundToInt(16f * Screen.height / 1080f);
        st.normal.textColor = new Color(0.86f, 0.83f, 0.78f, Mathf.Clamp01(shownTimer / 0.8f) * 0.8f);
        GUI.Label(new Rect(0, Screen.height - 110f * Screen.height / 1080f, Screen.width, 30), shownLine, st);
    }

    static Memento FirstActive()
    {
        foreach (Memento m in All) if (m != null && m.isActiveAndEnabled) return m;
        return null;
    }
}