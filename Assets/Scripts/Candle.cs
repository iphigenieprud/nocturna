using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

// Put this on the TOP object of the candle prefab.
// It doesn't hide or move anything. It only stops the candle's flame particles
// and switches off its lights when blown out, and starts them again when relit.
public class Candle : MonoBehaviour
{
    public static readonly List<Candle> All = new List<Candle>();
    public static event System.Action<Candle> AnyBlownOut;
    public static event System.Action<Candle> AnyLit;

    [Header("Candle")]
    public bool startLit = true;
    [Tooltip("Stops the pack's LOD Group from hiding the small flame when you're not right next to it.")]
    public bool alwaysShowFlame = true;

    [Header("Relighting")]
    public float relightDistance = 2.5f;
    public KeyCode relightKey = KeyCode.E;
    [Tooltip("What it is, shown on top when you're close.")]
    public string title = "a cold candle";
    [Tooltip("What to do, shown under it.")]
    public string prompt = "[E]   light it";

    [Header("Smoke (use the same smoke material as the ghost)")]
    public Material smokeMaterial;
    [Tooltip("Smoke puffed out of the flame while she's blowing on it.")]
    public Color blowSmokeColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
    [Tooltip("How much smoke at the start and end of her blowing.")]
    public Vector2 blowSmokeAmount = new Vector2(5f, 40f);

    [Header("Smoke when the candle goes out")]
    public Color plumeColor = new Color(0.55f, 0.55f, 0.55f, 0.3f);
    [Tooltip("Seconds the thin wisp of smoke keeps rising.")]
    public float plumeTime = 2.5f;

    [Header("She collects the smoke")]
    [Tooltip("Seconds the smoke rises on its own before she draws it in.")]
    public float pushTime = 1.2f;
    public float pullSpeed = 2.5f;
    public float pullStrength = 3f;
    public float absorbDistance = 0.35f;

    [Header("Smoke swirls around her")]
    [Tooltip("How wide the smoke circles around her.")]
    public float swirlRadius = 0.7f;
    [Tooltip("How fast the smoke circles (meters per second).")]
    public float swirlSpeed = 2.5f;
    [Tooltip("Seconds the smoke circles around her before it sinks in.")]
    public float swirlTime = 4f;
    [Tooltip("How far up and down her body the swirl spreads.")]
    public float swirlHeight = 0.6f;

    public bool IsLit { get; private set; }
    readonly HoldInteract hold = new HoldInteract();
    public bool BeingBlown { get; private set; }
    public Vector3 FlamePosition { get; private set; }

    readonly List<ParticleSystem> flames = new List<ParticleSystem>();
    readonly List<Light> lights = new List<Light>();
    readonly List<float> lightBase = new List<float>();
    readonly List<bool> lightStartOn = new List<bool>();

    ParticleSystem plume;
    ParticleSystem blowSmoke;
    Transform blower;
    GhostSmokeBody collector;
    readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[200];
    Transform player;
    bool warnedStopped;
    float blowProgress;
    float seed;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        seed = Random.value * 100f;

        GameObject p = GameObject.Find("PLAYER");
        if (p == null) p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        else Debug.LogWarning("Candle: no object called PLAYER or tagged Player found. Using the camera to check distance instead.", this);

        // Keep the flame at full detail at any distance
        if (alwaysShowFlame)
            foreach (LODGroup lod in GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0);

        // The candle's own flames and lights
        flames.AddRange(GetComponentsInChildren<ParticleSystem>(true));

        // Make sure the flame keeps burning steadily instead of fading out over time
        foreach (ParticleSystem ps in flames) MakeSteady(ps);

        // Tell us about any other scripts on the candle that could be turning it off
        List<string> others = new List<string>();
        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && !(mb is Candle)) others.Add(mb.GetType().Name + " (on " + mb.gameObject.name + ")");
        if (others.Count > 0)
            Debug.Log("Candle '" + name + "' also has these scripts, which might turn it off: " + string.Join(", ", others), this);
        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            lights.Add(l);
            lightBase.Add(l.intensity);
            lightStartOn.Add(l.enabled);
        }

        // Where the flame is: the first flame particle, or just above the candle
        FlamePosition = flames.Count > 0 ? flames[0].transform.position : transform.position + Vector3.up * 0.25f;

        if (smokeMaterial != null)
        {
            plume = CreatePlume();
            blowSmoke = CreateBlowSmoke();
        }

        if (startLit) LightCandle();
        else SetFlame(false);
    }

    void Update()
    {
        if (flames.Count > 0) FlamePosition = flames[0].transform.position;

        // Smoke drifts out, then gets pulled into her
        CollectSmoke(blowSmoke);
        CollectSmoke(plume);

        // Keep the flame alive while the candle is lit
        if (IsLit)
        {
            foreach (ParticleSystem ps in flames)
            {
                if (ps == null || (ps.isPlaying && ps.isEmitting)) continue;

                if (!warnedStopped)
                {
                    warnedStopped = true;
                    var m = ps.main;
                    Debug.LogWarning("Candle '" + name + "': flame '" + ps.name + "' stopped by itself, restarting it. "
                        + "Duration " + m.duration + "s, Looping " + m.loop
                        + ", Playing " + ps.isPlaying + ", Emitting " + ps.isEmitting, ps);
                }

                ps.Play(true);
            }
        }

        // Hold E when close (with empty hands) to light it
        bool close = !IsLit && DistanceToPlayer() < relightDistance;
        if (hold.Tick(this, close, title, prompt, relightKey)) LightCandle();

        // While she's blowing, the light struggles and dims
        if (IsLit && BeingBlown)
        {
            float n = Mathf.PerlinNoise(Time.time * 8f, seed);
            float strength = (0.6f + 0.4f * n) * Mathf.Lerp(1f, Random.Range(0.15f, 1f), blowProgress) * (1f - 0.6f * blowProgress);
            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null) lights[i].intensity = lightBase[i] * strength;

            UpdateBlowSmoke();
        }
    }

    void CollectSmoke(ParticleSystem ps)
    {
        if (ps == null || collector == null) return;

        int n = ps.GetParticles(buffer);
        if (n == 0) return;

        Vector3 target = collector.HeartPosition;
        float dt = Time.deltaTime;
        int absorbed = 0;
        float swirlZone = swirlRadius * 2f;

        for (int i = 0; i < n; i++)
        {
            // First the smoke just rises naturally
            float age = buffer[i].startLifetime - buffer[i].remainingLifetime;
            if (age < pushTime) continue;

            // Each puff ends up at its own height along her body
            float seed = (buffer[i].randomSeed % 1000) / 1000f;
            float targetY = target.y + (seed - 0.6f) * 2f * swirlHeight;

            Vector3 pos = buffer[i].position;
            Vector3 flat = pos - target;
            flat.y = 0f;
            float r = flat.magnitude;
            Vector3 outward = r > 0.001f ? flat / r : Vector3.forward;

            // Swallowed
            if (r < absorbDistance || age > pushTime + 12f)
            {
                buffer[i].remainingLifetime = 0f;
                absorbed++;
                continue;
            }

            Vector3 want;
            float follow;

            if (r > swirlZone)
            {
                // Drifts toward her
                Vector3 to = new Vector3(target.x, targetY, target.z) - pos;
                want = to.normalized * pullSpeed;
                follow = pullStrength;
            }
            else
            {
                // Close to her: circles around her body and slowly sinks in, faster as it gets closer
                float spin = swirlSpeed * Mathf.Clamp(swirlRadius / Mathf.Max(r, 0.15f), 1f, 3f);
                Vector3 around = Vector3.Cross(Vector3.up, outward) * spin;
                Vector3 inward = -outward * (swirlZone / Mathf.Max(0.5f, swirlTime));
                Vector3 upDown = Vector3.up * (targetY - pos.y) * 1.5f;
                want = around + inward + upDown;
                follow = pullStrength * 2f;
            }

            buffer[i].velocity = Vector3.Lerp(buffer[i].velocity, want, follow * dt);

            // Don't let the smoke fade out before she swallows it
            if (buffer[i].remainingLifetime < 0.5f) buffer[i].remainingLifetime = 0.5f;
        }

        ps.SetParticles(buffer, n);
        if (absorbed > 0) collector.Feed(absorbed);
    }

    Vector3 AwayFromBlower()
    {
        Vector3 away = blower != null ? FlamePosition - blower.position : Vector3.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
        return away.normalized;
    }

    // Smoke pushed out of the flame, away from her, thicker as the flame struggles
    void UpdateBlowSmoke()
    {
        if (blowSmoke == null) return;

        blowSmoke.transform.position = FlamePosition;
        blowSmoke.transform.rotation = Quaternion.LookRotation(AwayFromBlower() + Vector3.up * 0.6f);

        var emission = blowSmoke.emission;
        emission.rateOverTime = Mathf.Lerp(blowSmokeAmount.x, blowSmokeAmount.y, blowProgress);

        var main = blowSmoke.main;
        Color c = blowSmokeColor;
        c.a = Mathf.Lerp(blowSmokeColor.a * 0.4f, blowSmokeColor.a, blowProgress);
        main.startColor = c;
    }

    // ---------- Called by the ghost ----------

    public void StartBlow(Transform who = null)
    {
        if (!IsLit) return;
        BeingBlown = true;
        blower = who;
        collector = who != null ? who.GetComponent<GhostSmokeBody>() : null;
        if (blowSmoke != null)
        {
            UpdateBlowSmoke();
            blowSmoke.Play();
        }
    }

    void StopBlowSmoke()
    {
        if (blowSmoke != null) blowSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    public void SetBlowProgress(float p)
    {
        blowProgress = Mathf.Clamp01(p);
    }

    public void CancelBlow()
    {
        BeingBlown = false;
        blowProgress = 0f;
        ResetLights();
        StopBlowSmoke();
    }

    public void BlowOut()
    {
        if (!IsLit) return;

        IsLit = false;
        BeingBlown = false;
        blowProgress = 0f;

        // The full message in the Console shows which script called this
        Debug.Log("Candle '" + name + "' was blown out.", this);

        SetFlame(false);
        StopBlowSmoke();
        if (plume != null)
        {
            // A thin wisp rises straight up, then she draws it in
            plume.transform.position = FlamePosition;
            plume.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            plume.Play();
        }

        if (AnyBlownOut != null) AnyBlownOut(this);
    }

    // Flat distance (ignores height) from the player, or the camera if there's no player
    float DistanceToPlayer()
    {
        Vector3 from;
        if (player != null) from = player.position;
        else if (Camera.main != null) from = Camera.main.transform.position;
        else return Mathf.Infinity;

        // Measured to the flame itself, not the prefab's pivot (which can be far from the candle)
        Vector3 a = from; a.y = 0f;
        Vector3 b = FlamePosition; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void LightCandle()
    {
        IsLit = true;
        BeingBlown = false;
        blowProgress = 0f;
        warnedStopped = false;

        SetFlame(true);

        if (AnyLit != null) AnyLit(this);
    }

    // ---------- Keeping the pack's flame steady ----------

    // Turns "changes over time" settings into their strongest steady value,
    // so the flame doesn't fade or burn down by itself
    void MakeSteady(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true;
        main.stopAction = ParticleSystemStopAction.None;

        string before = "rate " + ps.emission.rateOverTime.mode
                      + ", lifetime " + main.startLifetime.mode
                      + ", size " + main.startSize.mode;

        main.startLifetime = Steady(main.startLifetime);
        main.startSize = Steady(main.startSize);
        main.startSpeed = Steady(main.startSpeed);

        var emission = ps.emission;
        ParticleSystem.MinMaxCurve rate = Steady(emission.rateOverTime);

        // A flame that only uses one burst at the very start: give it a steady rate instead
        if (MaxOf(rate) < 0.01f) rate = new ParticleSystem.MinMaxCurve(15f);
        emission.rateOverTime = rate;

        Debug.Log("Candle '" + name + "': made flame '" + ps.name + "' steady (it used: " + before + ").", ps);
    }

    ParticleSystem.MinMaxCurve Steady(ParticleSystem.MinMaxCurve c)
    {
        switch (c.mode)
        {
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                return new ParticleSystem.MinMaxCurve(MaxOf(c));
            default:
                return c;
        }
    }

    float MaxOf(ParticleSystem.MinMaxCurve c)
    {
        switch (c.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return c.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return c.constantMax;
            default:
                float best = 0f;
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    best = Mathf.Max(best, c.Evaluate(t, 0f), c.Evaluate(t, 1f));
                }
                return best;
        }
    }

    // ---------- Flame on / off ----------

    void SetFlame(bool on)
    {
        foreach (ParticleSystem ps in flames)
        {
            if (ps == null) continue;
            if (on) ps.Play(true);
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        for (int i = 0; i < lights.Count; i++)
        {
            if (lights[i] == null) continue;
            lights[i].enabled = on && lightStartOn[i];
            lights[i].intensity = lightBase[i];
        }
    }

    void ResetLights()
    {
        for (int i = 0; i < lights.Count; i++)
            if (lights[i] != null) lights[i].intensity = lightBase[i];
    }

    // ---------- Smoke plume ----------

    ParticleSystem CreateBlowSmoke()
    {
        GameObject go = new GameObject("Candle Blow Smoke");
        go.transform.SetParent(transform, false);
        go.transform.position = FlamePosition;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor = blowSmokeColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.rateOverTime = blowSmokeAmount.x;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.02f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.3f, 2f), new Keyframe(1f, 1.2f)));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 1.2f;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = smokeMaterial;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        return ps;
    }

    // The thin wisp of smoke that rises when a candle goes out
    ParticleSystem CreatePlume()
    {
        GameObject go = new GameObject("Candle Plume");
        go.transform.SetParent(transform, false);
        go.transform.position = FlamePosition;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = plumeTime;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
        main.startColor = plumeColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 200;

        // A steady thin stream that slowly runs out
        var emission = ps.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(45f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.6f), new Keyframe(1f, 0f)));

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 2f;
        shape.radius = 0.003f;

        // Warm air keeps lifting it
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.12f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        // Faint at the flame, softest at the top
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        // Starts as a thin thread, spreads out as it rises
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(8f, new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 1f)));

        // Gentle curls, like real candle smoke
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 2f;
        noise.scrollSpeed = 0.4f;
        noise.octaveCount = 2;
        noise.damping = true;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = smokeMaterial;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        return ps;
    }
}