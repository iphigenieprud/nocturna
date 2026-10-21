using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

// Put this on the ghost's top object.
// Turns her into a figure made of smoke particles that follow her body and animation,
// with a glowing heart of light in her chest that beats at random.
public class GhostSmokeBody : MonoBehaviour
{
    [Header("Smoke Material (Particles/Unlit, Transparent, GhostSmoke texture)")]
    public Material smokeMaterial;

    [Header("Which Body Parts Make Smoke")]
    [Tooltip("Leave empty to pick automatically (skips hair, eyelashes, eyebrows, eyes, teeth).")]
    public SkinnedMeshRenderer[] emitFrom;

    [Header("Body Shape")]
    public bool hideRealBody = true;
    public Color bodyColor = new Color(0f, 0f, 0f, 0.55f);
    public float density = 400f;
    public Vector2 puffSize = new Vector2(0.1f, 0.25f);
    public Vector2 puffLife = new Vector2(0.5f, 1.0f);
    public float churn = 0.15f;

    [Header("Head")]
    [Tooltip("1 = normal smoke around the head, 0 = almost none.")]
    [Range(0f, 1f)] public float headSmoke = 0.2f;
    public float headRadius = 0.25f;
    [Tooltip("Optional. Drag her Head bone here if the smoke isn't thinning around her head.")]
    public Transform headOverride;

    [Header("Rising Wisps")]
    public Color wispColor = new Color(0f, 0f, 0f, 0.35f);
    public float wispAmount = 20f;
    public float wispRise = 0.3f;

    [Header("Heart")]
    public bool heartLight = true;
    public Color heartColor = new Color(0.6f, 0.75f, 1f);
    [Range(0f, 10f)] public float heartGlow = 4f;
    public float heartSize = 0.15f;
    public bool heartShowsThroughSmoke = true;
    public float heartIntensity = 1.5f;
    public float heartRange = 2f;

    [Header("Heartbeat")]
    [Tooltip("Beats per minute. 60-70 is a calm resting heart.")]
    [Range(30f, 120f)] public float heartBPM = 55f;
    [Range(0f, 1f)] public float heartSwell = 0.25f;
    [Tooltip("Higher = the heart follows her chest more smoothly and is easier to aim at.")]
    [Range(0f, 1f)] public float heartSteadiness = 0.85f;

    static readonly string[] skipNames = { "hair", "brow", "lash", "eye", "teeth", "tooth", "tongue" };

    readonly List<ParticleSystem> bodySystems = new List<ParticleSystem>();
    readonly List<ParticleSystem> wispSystems = new List<ParticleSystem>();
    ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[3000];

    Transform headBone;

    Light heart;
    Transform heartRoot;
    Transform heartAnchor;
    Vector3 heartAnchorOffset;
    ParticleSystem heartGlowPS;
    ParticleSystem heartCorePS;
    Material heartGlowMat;
    Material heartCoreMat;

    void Start()
    {
        List<SkinnedMeshRenderer> parts = PickBodyParts();
        foreach (SkinnedMeshRenderer s in parts) s.updateWhenOffscreen = true;

        headBone = headOverride;
        if (headBone == null)
        {
            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman) headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        }
        if (headBone == null)
            Debug.LogWarning("GhostSmokeBody: couldn't find her head bone. Drag it into 'Head Override'.");

        if (hideRealBody) HideBody();

        if (parts.Count > 0)
        {
            // Split the smoke between parts by surface area, so density is even everywhere
            float[] areas = new float[parts.Count];
            float total = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                areas[i] = SurfaceArea(parts[i]);
                total += areas[i];
            }

            for (int i = 0; i < parts.Count; i++)
            {
                float share = total > 0f ? areas[i] / total : 1f / parts.Count;

                ParticleSystem b = NewSystem("Smoke Body (" + parts[i].name + ")", smokeMaterial);
                SetupBody(b, parts[i], null, share);
                bodySystems.Add(b);

                ParticleSystem w = NewSystem("Smoke Wisps (" + parts[i].name + ")", smokeMaterial);
                SetupWisps(w, parts[i], null, share);
                wispSystems.Add(w);
            }
        }
        else
        {
            MeshRenderer mesh = GetComponentInChildren<MeshRenderer>();

            ParticleSystem b = NewSystem("Smoke Body", smokeMaterial);
            SetupBody(b, null, mesh, 1f);
            bodySystems.Add(b);

            ParticleSystem w = NewSystem("Smoke Wisps", smokeMaterial);
            SetupWisps(w, null, mesh, 1f);
            wispSystems.Add(w);
        }

        if (heartLight) CreateHeart();
    }

    void Update()
    {
        UpdateHeart();
    }

    void LateUpdate()
    {
        ThinHeadSmoke();
        MoveHeart();
    }

    void OnDestroy()
    {
        // The heart isn't a child of her (so it can move smoothly), so clean it up here
        if (heartRoot != null) Destroy(heartRoot.gameObject);
    }

    // ---------- Less smoke around the head ----------

    void ThinHeadSmoke()
    {
        if (headBone == null || headSmoke >= 0.999f) return;

        Vector3 head = headBone.position + Vector3.up * 0.08f;
        float r2 = headRadius * headRadius;

        // Thin everything passing through the head, including wisps rising from the body
        foreach (ParticleSystem ps in bodySystems) Thin(ps, head, r2);
        foreach (ParticleSystem ps in wispSystems) Thin(ps, head, r2);
    }

    void Thin(ParticleSystem ps, Vector3 head, float r2)
    {
        if (ps == null) return;
        int n = ps.GetParticles(buffer);
        bool changed = false;

        for (int i = 0; i < n; i++)
        {
            if ((buffer[i].position - head).sqrMagnitude > r2) continue;

            // Each puff gets a fixed random number, so the decision doesn't flicker
            float keep = (buffer[i].randomSeed % 1000) / 1000f;
            if (keep > headSmoke)
            {
                buffer[i].remainingLifetime = 0f;
                changed = true;
            }
        }

        if (changed) ps.SetParticles(buffer, n);
    }

    // ---------- Picking body parts ----------

    List<SkinnedMeshRenderer> PickBodyParts()
    {
        List<SkinnedMeshRenderer> list = new List<SkinnedMeshRenderer>();

        if (emitFrom != null && emitFrom.Length > 0)
        {
            foreach (SkinnedMeshRenderer s in emitFrom)
                if (s != null && s.sharedMesh != null) list.Add(s);
            return list;
        }

        SkinnedMeshRenderer biggest = null;
        int biggestCount = 0;

        foreach (SkinnedMeshRenderer s in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (s.sharedMesh == null) continue;

            if (s.sharedMesh.vertexCount > biggestCount)
            {
                biggest = s;
                biggestCount = s.sharedMesh.vertexCount;
            }

            string n = s.name.ToLower();
            bool skip = false;
            foreach (string word in skipNames)
                if (n.Contains(word)) { skip = true; break; }

            if (!skip) list.Add(s);
        }

        if (list.Count == 0 && biggest != null) list.Add(biggest);
        return list;
    }

    float SurfaceArea(SkinnedMeshRenderer s)
    {
        Mesh m = s.sharedMesh;
        Vector3 scale = s.transform.lossyScale;

        if (m.isReadable)
        {
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            float area = 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = Vector3.Scale(v[t[i]], scale);
                Vector3 b = Vector3.Scale(v[t[i + 1]], scale);
                Vector3 c = Vector3.Scale(v[t[i + 2]], scale);
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            return area;
        }

        // Fallback when the mesh can't be read: estimate from its bounding box
        Vector3 size = Vector3.Scale(m.bounds.size, scale);
        return 2f * (size.x * size.y + size.y * size.z + size.z * size.x);
    }

    // ---------- Body made of smoke ----------

    void SetupBody(ParticleSystem ps, SkinnedMeshRenderer skin, MeshRenderer mesh, float share)
    {
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(puffLife.x, puffLife.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(puffSize.x, puffSize.y);
        main.startColor = bodyColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 3000;

        var emission = ps.emission;
        emission.rateOverTime = density * share;

        ShapeFromBody(ps, skin, mesh);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        AddLook(ps, 0.8f, 1.4f, churn, 1.5f);
    }

    void SetupWisps(ParticleSystem ps, SkinnedMeshRenderer skin, MeshRenderer mesh, float share)
    {
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startColor = wispColor;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = wispAmount * share;

        ShapeFromBody(ps, skin, mesh);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(wispRise * 0.5f, wispRise);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        AddLook(ps, 0.6f, 1.8f, 0.3f, 0.4f);
    }

    // ---------- Heart: glowing wisps of light ----------

    void CreateHeart()
    {
        heartAnchor = transform;
        heartAnchorOffset = new Vector3(0f, 1.3f, 0f);

        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            Transform chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chest != null)
            {
                heartAnchor = chest;
                heartAnchorOffset = Vector3.zero;
            }
        }

        // The heart follows her chest smoothly instead of being stuck to the bone
        GameObject root = new GameObject(name + " Heart");
        heartRoot = root.transform;
        heartRoot.position = heartAnchor.TransformPoint(heartAnchorOffset);

        if (smokeMaterial != null)
        {
            int queue = heartShowsThroughSmoke ? 3100 : 3000;

            heartGlowMat = new Material(smokeMaterial);
            heartGlowMat.renderQueue = queue;
            heartGlowPS = NewHeartSystem("Heart Glow", heartGlowMat);
            SetupHeartLayer(heartGlowPS, 14f, new Vector2(0.6f, 1.1f), new Vector2(0.8f, 1.4f), 0.3f);

            heartCoreMat = new Material(smokeMaterial);
            heartCoreMat.renderQueue = queue + 1;
            heartCorePS = NewHeartSystem("Heart Core", heartCoreMat);
            SetupHeartLayer(heartCorePS, 10f, new Vector2(0.25f, 0.45f), new Vector2(0.6f, 1.0f), 0.1f);
        }

        GameObject go = new GameObject("Heart Light");
        go.transform.SetParent(heartRoot, false);

        heart = go.AddComponent<Light>();
        heart.type = LightType.Point;
        heart.shadows = LightShadows.None;

        UpdateHeart();
    }

    ParticleSystem NewHeartSystem(string label, Material mat)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(heartRoot, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.material = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        return ps;
    }

    void SetupHeartLayer(ParticleSystem ps, float rate, Vector2 sizeRange, Vector2 life, float spread)
    {
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(heartSize * sizeRange.x, heartSize * sizeRange.y);
        main.startColor = Color.white;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 60;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = heartSize * spread;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sizeLife = ps.sizeOverLifetime;
        sizeLife.enabled = true;
        AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(1f, 1.15f));
        sizeLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

        // Slow, calm swirl
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = heartSize * 0.15f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.3f;

        ps.Play();
    }

    void MoveHeart()
    {
        if (heartRoot == null || heartAnchor == null) return;

        Vector3 target = heartAnchor.TransformPoint(heartAnchorOffset);
        float speed = Mathf.Lerp(30f, 3f, heartSteadiness);
        heartRoot.position = Vector3.Lerp(heartRoot.position, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    // Steady "lub-dub" heartbeat at the chosen BPM, 0 to 1
    float Beat()
    {
        float secondsPerBeat = 60f / Mathf.Max(1f, heartBPM);
        float t = Mathf.Repeat(Time.time, secondsPerBeat);
        float lub = Mathf.Exp(-Mathf.Pow((t - 0.05f) * 14f, 2f));
        float dub = Mathf.Exp(-Mathf.Pow((t - 0.3f) * 14f, 2f)) * 0.6f;
        return Mathf.Clamp01(lub + dub);
    }

    void UpdateHeart()
    {
        if (heartRoot == null) return;

        float beat = Beat();
        float flicker = 0.92f + 0.16f * Mathf.PerlinNoise(Time.time * 1.2f, 0.37f);
        float glow = heartGlow * (0.5f + beat) * flicker;

        SetGlowColor(heartGlowMat, heartColor, glow * 0.6f, 0.5f);
        SetGlowColor(heartCoreMat, Color.Lerp(heartColor, Color.white, 0.5f), glow * 1.5f, 0.9f);

        heartRoot.localScale = Vector3.one * (1f + heartSwell * beat);

        if (heart != null)
        {
            heart.color = heartColor;
            heart.range = heartRange;
            heart.intensity = heartIntensity * (0.5f + beat) * flicker;
        }
    }

    void SetGlowColor(Material m, Color c, float strength, float alpha)
    {
        if (m == null) return;
        Color hdr = new Color(c.r * strength, c.g * strength, c.b * strength, alpha);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", hdr);
        if (m.HasProperty("_Color")) m.SetColor("_Color", hdr);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", hdr);
    }

    public void SetHeart(bool on)
    {
        if (on && heartRoot == null) CreateHeart();
        if (heartRoot != null) heartRoot.gameObject.SetActive(on);
    }

    // Where the heart is right now (useful later for aiming and hit checks)
    public Vector3 HeartPosition
    {
        get { return heartRoot != null ? heartRoot.position : transform.position + Vector3.up * 1.3f; }
    }

    // ---------- Helpers ----------

    void HideBody()
    {
        Shader spectre = Shader.Find("Custom/Spectre");
        Material invisible = null;
        if (spectre != null)
        {
            invisible = new Material(spectre);
            invisible.SetFloat("_Visibility", 0f);
        }

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;

            r.shadowCastingMode = ShadowCastingMode.Off;

            if (invisible != null)
            {
                // Keeps the mesh animating (so the smoke follows it) but draws nothing
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = invisible;
                r.sharedMaterials = mats;
            }
            else
            {
                r.forceRenderingOff = true;
            }
        }
    }

    void ShapeFromBody(ParticleSystem ps, SkinnedMeshRenderer skin, MeshRenderer mesh)
    {
        var shape = ps.shape;
        if (skin != null)
        {
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = skin;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Random;
        }
        else if (mesh != null)
        {
            shape.shapeType = ParticleSystemShapeType.MeshRenderer;
            shape.meshRenderer = mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Random;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            ps.transform.localPosition = new Vector3(0f, 1f, 0f);
        }
    }

    ParticleSystem NewSystem(string label, Material mat)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortMode = ParticleSystemSortMode.Distance;
        rend.material = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        return ps;
    }

    void AddLook(ParticleSystem ps, float startScale, float endScale, float noiseStrength, float noiseFrequency)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sizeLife = ps.sizeOverLifetime;
        sizeLife.enabled = true;
        AnimationCurve curve = new AnimationCurve(new Keyframe(0f, startScale), new Keyframe(1f, endScale));
        sizeLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = noiseStrength;
        noise.frequency = noiseFrequency;
        noise.scrollSpeed = 0.5f;
        noise.damping = true;

        ps.Play();
    }

    // Lets other scripts stop the smoke, for example when she dies
    public void StopSmoke()
    {
        foreach (ParticleSystem ps in bodySystems)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (ParticleSystem ps in wispSystems)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        SetHeart(false);
    }
}