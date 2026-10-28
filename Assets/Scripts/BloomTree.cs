using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Put this on the tomb tree.
// Flowering ivy that you placed by hand climbs the tree as candles are lit,
// from the lowest pieces to the highest. It only grows where you're not looking,
// so you turn around and find it has grown, with a little pollen left in the air.
// When she blows a candle out, the highest pieces vanish (also out of sight) and drop petals.
// All candles lit = full bloom.
//
// No tree yet? Click the three dots on this component and choose "Build Placeholder Tree".
public class BloomTree : MonoBehaviour
{
    public static BloomTree Instance;

    [Header("Ivy")]
    [Tooltip("The object holding all your ivy. Every separate bit grows on its own, from the lowest to the highest.")]
    public Transform ivy;

    [Header("Healing")]
    [Tooltip("Being this close to the tree heals you, and calms her.")]
    public float healRadius = 6f;
    [Tooltip("Health per second you get back near the tree at full bloom. Only works once it has flowers.")]
    public float playerHealPerSecond = 4f;
    [Tooltip("How much paler she gets per second while near the tree. 0.06 = about a third lighter after one 8-second rest.")]
    public float herLightenPerSecond = 0.06f;
    [Tooltip("Seconds near the tree for her to heal one heart hit.")]
    public float herSecondsPerHeal = 12f;

    [Header("Particles")]
    [Tooltip("Soft round particle material for pollen and petals. GhostFogMat works.")]
    public Material blossomMaterial;
    public Color petalColor = new Color(1f, 0.97f, 0.95f, 0.9f);
    public Color pollenColor = new Color(1f, 0.9f, 0.7f, 1f);

    // How much of the ivy is showing, 0 to 1
    public float Bloom { get { return pieces.Count > 0 ? shown / pieces.Count : 0f; } }

    // Every candle in the scene is lit
    public bool FullBloom
    {
        get
        {
            if (Candle.All.Count == 0) return false;
            foreach (Candle c in Candle.All) if (c != null && !c.IsLit) return false;
            return true;
        }
    }

    const float NewPiecesPerSecond = 8f;   // how fast the ivy climbs
    const float MaxWaitWhileWatched = 8f;  // seconds before it grows even while you watch
    const float SeeDistance = 40f;         // farther than this, the fog hides it anyway
    const float PetalsAtFullBloom = 4f;    // petals drifting down per second
    const float PollenAtFullBloom = 8f;    // glowing motes floating around per second
    const float PollenWhileHealing = 30f;  // extra pollen per second drifting to whoever is healing

    readonly List<Transform> pieces = new List<Transform>();
    readonly List<Vector3> fullScale = new List<Vector3>();
    readonly List<Bounds> area = new List<Bounds>();
    float[] waiting;
    ParticleSystem petals, pollen;
    float shown;

    void Awake() { Instance = this; }

    void Start()
    {
        CollectIvy();

        if (blossomMaterial != null)
        {
            petals = MakeParticles("Falling Petals", blossomMaterial, true);
            Material glow = new Material(blossomMaterial);
            Color hdr = pollenColor * 3f;
            hdr.a = 1f;
            if (glow.HasProperty("_BaseColor")) glow.SetColor("_BaseColor", hdr);
            if (glow.HasProperty("_Color")) glow.SetColor("_Color", hdr);
            pollen = MakeParticles("Pollen", glow, false);
        }
        else Debug.LogWarning("BloomTree: drag GhostFogMat into 'Blossom Material' for the pollen and petal effects.");
    }

    void Update()
    {
        float dt = Time.deltaTime;
        HealNearby(dt);
        if (pieces.Count == 0) return;

        // How much ivy there should be: one share per lit candle
        int total = 0, lit = 0;
        foreach (Candle c in Candle.All)
        {
            if (c == null) continue;
            total++;
            if (c.IsLit) lit++;
        }
        // Round up, so even one lit candle always shows some ivy
        float target = total > 0 ? Mathf.Ceil(pieces.Count * (float)lit / total) : 0f;
        if (blossomed) target = pieces.Count;
        shown = Mathf.MoveTowards(shown, target, NewPiecesPerSecond * dt);
        int wanted = Mathf.FloorToInt(shown + 0.001f);

        // Ivy only appears or disappears where you're not looking,
        // so you turn around and find it has grown
        Camera cam = Camera.main;
        Plane[] view = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;

        for (int i = 0; i < pieces.Count; i++)
        {
            bool shouldShow = i < wanted;
            bool isShown = pieces[i].gameObject.activeSelf;
            if (shouldShow == isShown) { waiting[i] = 0f; continue; }

            if (blossomed)
            {
                // The final bloom: grows in plain sight, climbing the tree
                if (shouldShow) Pollen(i, 6);
            }
            else if (Seen(i, cam, view))
            {
                // Being watched: wait. If you stare for too long, it happens anyway, hidden in a puff of pollen.
                waiting[i] += dt;
                if (waiting[i] < MaxWaitWhileWatched) continue;
                if (shouldShow) Pollen(i, 10);
            }
            else if (shouldShow) Pollen(i, 2); // just a little pollen left behind

            if (!shouldShow) Petals(i, 4);
            pieces[i].gameObject.SetActive(shouldShow);
            waiting[i] = 0f;
        }

        // Petals and pollen drift around the tree: a little at first, more as the ivy grows
        float bloom = Bloom;
        if (wanted > 0)
        {
            if (Random.value < PetalsAtFullBloom * bloom * bloom * dt) Petals(Random.Range(0, wanted), 1);
            if (Random.value < PollenAtFullBloom * bloom * bloom * dt) Pollen(Random.Range(0, wanted), 1);
        }
    }

    // Is this piece on screen and close enough to see through the fog?
    bool Seen(int i, Camera cam, Plane[] view)
    {
        if (cam == null) return false;
        if (Vector3.Distance(cam.transform.position, area[i].center) > SeeDistance) return false;
        return GeometryUtility.TestPlanesAABB(view, area[i]);
    }

    // ---------- Healing ----------

    // A bare tree doesn't heal. The more flowering ivy, the stronger it heals.
    float HealStrength { get { return Bloom; } }

    // The tree has flowers (and pollen), so it can heal
    public bool CanHeal { get { return Bloom > 0.001f; } }

    public bool IsNear(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.magnitude < healRadius;
    }

    ShadowPerson ghost;

    void HealNearby(float dt)
    {
        if (!CanHeal) return;
        PlayerHealth player = PlayerHealth.Instance;
        if (player != null && IsNear(player.transform.position)
            && player.Heal(playerHealPerSecond * HealStrength * dt))
            PollenTo(player.transform.position + Vector3.up * 1.2f, dt);

        if (ghost == null) ghost = FindObjectOfType<ShadowPerson>();
        if (ghost != null && IsNear(ghost.transform.position)
            && ghost.TreeHeal(dt, Mathf.Max(0.5f, HealStrength), herLightenPerSecond, herSecondsPerHeal))
            PollenTo(ghost.transform.position + Vector3.up * 1.2f, dt);
    }

    // While someone heals, the tree releases extra pollen that drifts toward them
    void PollenTo(Vector3 target, float dt)
    {
        int showing = 0;
        for (int i = 0; i < pieces.Count; i++) if (pieces[i].gameObject.activeSelf) showing++;
        if (pollen == null || showing == 0) return;

        float amount = PollenWhileHealing * dt;
        int count = Mathf.FloorToInt(amount) + (Random.value < amount % 1f ? 1 : 0);
        for (int n = 0; n < count; n++)
        {
            // Pick a random piece of ivy that's showing
            int i = Random.Range(0, pieces.Count);
            for (int tries = 0; tries < 10 && !pieces[i].gameObject.activeSelf; tries++) i = Random.Range(0, pieces.Count);
            if (!pieces[i].gameObject.activeSelf) continue;

            Vector3 from = PointOn(i);
            float life = Random.Range(2f, 3.5f);
            var p = new ParticleSystem.EmitParams();
            p.position = from;
            p.velocity = (target + Random.insideUnitSphere * 0.4f - from) / life; // arrives as it fades
            p.startSize = Random.Range(0.02f, 0.05f);
            p.startLifetime = life;
            p.applyShapeToPosition = false;
            pollen.Emit(p, 1);
        }
    }

    // The final moment: every piece of ivy appears at once, with a storm of petals and pollen
    bool blossomed;
    // The ivy climbs the whole tree in front of you, from the roots to the top, each piece with a puff of pollen
    public void Blossom()
    {
        blossomed = true;
        for (int i = 0; i < pieces.Count; i++) Petals(i, 2);
    }

    // A spot beside the tree where she can stand, on the open side (toward the player, away from the mountain)
    public Vector3 RestSpot(Vector3 toward)
    {
        Vector3 dir = toward - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        dir = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * dir.normalized;
        return transform.position + dir * Random.Range(1.5f, 2.5f);
    }

    // ---------- Ivy pieces ----------

    void CollectIvy()
    {
        if (ivy == null) ivy = transform.Find("Ivy");
        if (ivy == null)
        {
            Debug.LogWarning("BloomTree: drag the object holding your ivy pieces into the 'Ivy' slot.");
            return;
        }

        foreach (Transform child in FindPieces())
        {
            Renderer[] rends = child.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;

            // Remember each piece's full size and where it is (for the particles)
            Bounds b = rends[0].bounds;
            foreach (Renderer r in rends) b.Encapsulate(r.bounds);

            pieces.Add(child);
            fullScale.Add(child.localScale);
            area.Add(b);

            // Decoration only: no colliders, so it doesn't block bullets or the ghost
            foreach (Collider col in child.GetComponentsInChildren<Collider>()) Destroy(col);
        }

        // Lowest first, so the ivy climbs
        List<int> order = new List<int>();
        for (int i = 0; i < pieces.Count; i++) order.Add(i);
        order.Sort((x, y) => area[x].min.y.CompareTo(area[y].min.y));

        List<Transform> p = new List<Transform>();
        List<Vector3> s = new List<Vector3>();
        List<Bounds> a = new List<Bounds>();
        foreach (int i in order) { p.Add(pieces[i]); s.Add(fullScale[i]); a.Add(area[i]); }
        pieces.Clear(); pieces.AddRange(p);
        fullScale.Clear(); fullScale.AddRange(s);
        area.Clear(); area.AddRange(a);

        waiting = new float[pieces.Count];
        foreach (Transform t in pieces) t.gameObject.SetActive(false);

        Debug.Log("BloomTree: found " + pieces.Count + " ivy pieces.");
    }

    // Every separate bit of ivy becomes its own piece, even inside one big prefab,
    // so the ivy can climb bit by bit. Objects with LOD levels stay whole.
    List<Transform> FindPieces()
    {
        List<Transform> found = new List<Transform>();
        foreach (LODGroup lod in ivy.GetComponentsInChildren<LODGroup>())
            if (!found.Exists(f => lod.transform.IsChildOf(f))) found.Add(lod.transform);

        foreach (Renderer r in ivy.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (found.Exists(f => r.transform.IsChildOf(f))) continue;
            found.Add(r.transform);
        }
        return found;
    }

    // A random point inside one ivy piece
    Vector3 PointOn(int i)
    {
        Bounds b = area[i];
        return b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.8f);
    }

    // ---------- Particles ----------

    void Pollen(int piece, int count)
    {
        if (pollen == null) return;
        for (int n = 0; n < count; n++)
        {
            var p = new ParticleSystem.EmitParams();
            p.position = PointOn(piece);
            p.velocity = Random.insideUnitSphere * 0.15f + Vector3.up * 0.12f;
            p.startSize = Random.Range(0.02f, 0.05f);
            p.startLifetime = Random.Range(1.5f, 3f);
            p.applyShapeToPosition = false;
            pollen.Emit(p, 1);
        }
    }

    void Petals(int piece, int count)
    {
        if (petals == null) return;
        for (int n = 0; n < count; n++)
        {
            var p = new ParticleSystem.EmitParams();
            p.position = PointOn(piece);
            p.velocity = new Vector3(Random.Range(-0.1f, 0.1f), -0.05f, Random.Range(-0.1f, 0.1f));
            p.startSize = Random.Range(0.04f, 0.08f);
            p.startColor = petalColor;
            p.startLifetime = Random.Range(4f, 7f);
            p.applyShapeToPosition = false;
            petals.Emit(p, 1);
        }
    }

    ParticleSystem MakeParticles(string label, Material mat, bool falling)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;
        main.startSpeed = 0f;
        main.gravityModifier = falling ? 0.02f : -0.005f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;

        // Gentle swaying
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = falling ? 0.25f : 0.1f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.3f;

        if (falling)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        }

        // Fade in and out
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        ps.Play();
        return ps;
    }

    // ---------- Placeholder tree (until your real one is in) ----------

#if UNITY_EDITOR
    [ContextMenu("Build Placeholder Tree")]
    void BuildPlaceholder()
    {
        GameObject root = new GameObject("Placeholder Tree");
        Undo.RegisterCreatedObjectUndo(root, "Build Placeholder Tree");
        root.transform.SetParent(transform, false);

        // A crooked trunk, leaning a little
        Part(root.transform, "Trunk", new Vector3(0f, 1.6f, 0f), new Vector3(0f, 0f, 8f), new Vector3(0.45f, 1.6f, 0.45f), true);
        Part(root.transform, "Upper Trunk", new Vector3(-0.3f, 3.6f, 0f), new Vector3(0f, 0f, -12f), new Vector3(0.32f, 0.8f, 0.32f), false);

        // Twisted branches reaching out, each with a couple of twigs
        Random.InitState(7);
        for (int i = 0; i < 7; i++)
        {
            float angle = i * 360f / 7f + Random.Range(-20f, 20f);
            float tilt = Random.Range(35f, 65f);
            float height = Random.Range(3.0f, 4.3f);
            Quaternion rot = Quaternion.Euler(0f, angle, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            float len = Random.Range(0.9f, 1.4f);
            Vector3 basePos = new Vector3(-0.25f, height, 0f);
            Vector3 mid = basePos + rot * Vector3.up * len;

            Part(root.transform, "Branch " + (i + 1), mid, rot.eulerAngles, new Vector3(0.12f, len, 0.12f), false);

            for (int k = 0; k < 2; k++)
            {
                Quaternion twig = rot * Quaternion.Euler(Random.Range(-40f, 40f), 0f, Random.Range(-40f, 40f));
                float tl = Random.Range(0.4f, 0.7f);
                Vector3 start = basePos + rot * Vector3.up * len * Random.Range(1.3f, 1.9f);
                Part(root.transform, "Twig", start + twig * Vector3.up * tl, twig.eulerAngles, new Vector3(0.05f, tl, 0.05f), false);
            }
        }

        // An empty holder for the ivy, ready to fill
        if (transform.Find("Ivy") == null)
        {
            GameObject holder = new GameObject("Ivy");
            Undo.RegisterCreatedObjectUndo(holder, "Build Placeholder Tree");
            holder.transform.SetParent(transform, false);
            ivy = holder.transform;
            EditorUtility.SetDirty(this);
        }

        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("BloomTree: placeholder built. Put your ivy pieces inside 'Ivy'.");
    }

    void Part(Transform parent, string label, Vector3 pos, Vector3 euler, Vector3 scale, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = label;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        if (!keepCollider) DestroyImmediate(go.GetComponent<Collider>());
    }
#endif
}