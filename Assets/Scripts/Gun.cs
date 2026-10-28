using UnityEngine;
using UnityEngine.Rendering;

// A polished first-person gun:
// left click shoots, right click aims, R reloads.
// Muzzle flash, tracer, impacts, gun kick, dynamic crosshair, hit marker and ammo counter.
// Everything is made in code, so you only need to fill in the slots you want.
[DefaultExecutionOrder(1000)]
public class PlayerGun : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Leave empty to use the Main Camera.")]
    public Camera cam;
    [Tooltip("The gun model (the object under GunHolder). Used for the kick when you shoot.")]
    public Transform gunModel;
    [Tooltip("An empty object at the tip of the barrel. Leave empty and it's guessed.")]
    public Transform muzzle;
    [Tooltip("Same smoke material as the ghost (GhostFogMat). Used for the flash and impact smoke.")]
    public Material smokeMaterial;

    [Header("Animations (optional)")]
    [Tooltip("Turn on only if your player script does NOT already play the shoot and reload animations.")]
    public bool playAnimations = false;
    public Animator anim;
    public string shootTrigger = "Shoot";
    public string reloadTrigger = "Reload";

    [Header("Gun")]
    public int magazineSize = 12;
    [Tooltip("Seconds between shots.")]
    public float fireRate = 0.18f;
    [Tooltip("Hold to keep firing.")]
    public bool automatic = false;
    public float range = 120f;
    public float reloadTime = 1.6f;
    public float hitForce = 8f;
    [Tooltip("How big the target around her heart is (meters). Only shots inside it hurt her.")]
    public float heartHitSize = 0.2f;

    [Header("Accuracy (degrees)")]
    public float spread = 0.6f;
    public float aimSpread = 0.1f;
    public float moveSpread = 2.5f;
    [Tooltip("Extra spread added by each shot, recovers quickly.")]
    public float shotBloom = 1.2f;

    [Header("Aiming")]
    public float aimFOV = 45f;
    public float aimSpeed = 10f;

    [Header("Feel")]
    public float kickBack = 0.05f;
    public float kickUp = 5f;
    public float kickRecover = 14f;
    public Color flashColor = new Color(1f, 0.75f, 0.4f);
    public float flashBrightness = 6f;
    public bool showTracer = true;
    public Color tracerColor = new Color(1f, 0.9f, 0.7f, 0.6f);

    [Header("Moving")]
    [Tooltip("You can't reload while walking or running. Moving during a reload cancels it.")]
    public bool noReloadWhileMoving = true;
    [Tooltip("After running this many seconds, the arms and gun are put away (hidden). 0 = never.")]
    public float hideAfterRunning = 1f;

    [Header("Screen")]
    public bool showCrosshair = true;
    public Color crosshairColor = new Color(1f, 1f, 1f, 0.85f);
    public bool showAmmo = true;

    [Header("Sounds (optional)")]
    public AudioClip shootSound;
    public AudioClip reloadSound;
    public AudioClip emptySound;
    public AudioClip hitSound;
    [Range(0f, 1f)] public float volume = 0.8f;

    int ammo;
    bool reloading;
    float reloadTimer;
    float nextShotTime;
    bool aiming;
    float baseFOV;
    float bloom;
    float currentSpread;
    float kick;
    float hitMarkerTimer;
    float emptyBlink;
    Vector3 lastPos;
    float moveAmount;

    Vector3 gunRestPos;
    Quaternion gunRestRot;

    Light flashLight;
    float flashTimer;
    ParticleSystem flashFx, sparks, dust, ghostPuff;
    LineRenderer tracer;
    float tracerTimer;
    AudioSource audioSource;
    Texture2D pixel;

    // Set by WeaponSwitcher: true = the gun is put away (empty hands or another item)
    [HideInInspector] public bool holstered;
    float runTimer, away;   // away: 0 = arms and gun out, 1 = put away
    Transform leftArm, rightArm, head;

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null) baseFOV = cam.fieldOfView;
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            leftArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
        }

        ammo = magazineSize;
        lastPos = transform.position;

        if (gunModel != null)
        {
            gunRestPos = gunModel.localPosition;
            gunRestRot = gunModel.localRotation;
            if (muzzle == null) muzzle = FindChild(gunModel, "muzzle");
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        pixel = Texture2D.whiteTexture;

        BuildEffects();
    }

    void Update()
    {
        if (cam == null) return;
        float dt = Time.deltaTime;

        // How fast we're moving (for spread)
        float speed = (transform.position - lastPos).magnitude / Mathf.Max(dt, 0.0001f);
        lastPos = transform.position;
        moveAmount = Mathf.Lerp(moveAmount, Mathf.Clamp01(speed / 4f), 8f * dt);

        // Walking / running (from the keys, so it reacts instantly)
        bool moving = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
        bool running = moving && Input.GetKey(KeyCode.LeftShift);

        // Running for a while puts the arms and gun away; they come back when you stop running
        runTimer = running ? runTimer + dt : 0f;
        bool putAway = holstered || (hideAfterRunning > 0f && runTimer > hideAfterRunning);
        away = Mathf.MoveTowards(away, putAway ? 1f : 0f, dt * 5f);
        bool gunReady = away < 0.05f;

        // Moving (or putting the gun away) cancels a reload
        if (reloading && ((moving && noReloadWhileMoving) || holstered)) CancelReload();

        // Aim
        aiming = Input.GetMouseButton(1) && !reloading && gunReady;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, aiming ? aimFOV : baseFOV, aimSpeed * dt);

        // Reload (only while standing still)
        bool canReload = gunReady && !(moving && noReloadWhileMoving);
        if (Input.GetKeyDown(KeyCode.R) && !reloading && ammo < magazineSize && canReload) StartReload();
        if (reloading)
        {
            reloadTimer -= dt;
            if (reloadTimer <= 0f)
            {
                reloading = false;
                ammo = magazineSize;
            }
        }

        // Shoot
        bool pull = automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (pull && Time.time >= nextShotTime && !reloading && gunReady)
        {
            if (ammo > 0) Shoot();
            else
            {
                Play(emptySound, 1f);
                nextShotTime = Time.time + 0.3f;
                emptyBlink = 1f;
                if (Input.GetMouseButtonDown(0) && canReload) StartReload();
            }
        }

        // Spread settles back down
        bloom = Mathf.Lerp(bloom, 0f, 6f * dt);
        float target = (aiming ? aimSpread : spread) + moveSpread * moveAmount * (aiming ? 0.3f : 1f) + bloom;
        currentSpread = Mathf.Lerp(currentSpread, target, 12f * dt);

        // Effects fade
        kick = Mathf.Lerp(kick, 0f, kickRecover * dt);
        hitMarkerTimer -= dt;
        emptyBlink = Mathf.Max(0f, emptyBlink - dt);

        if (flashLight != null)
        {
            flashTimer -= dt;
            flashLight.intensity = flashTimer > 0f ? flashBrightness * (flashTimer / 0.06f) : 0f;
        }

        if (tracer != null)
        {
            tracerTimer -= dt;
            Color c = tracerColor;
            c.a *= Mathf.Clamp01(tracerTimer / 0.05f);
            tracer.startColor = c;
            tracer.endColor = new Color(c.r, c.g, c.b, 0f);
            tracer.enabled = tracerTimer > 0f;
        }

        // Tell the UI what's going on (if there is a UIManager)
        UIManager ui = UIManager.Instance;
        if (ui != null)
        {
            ui.SetAmmo(ammo, magazineSize);
            ui.SetReloading(reloading, 1f - Mathf.Clamp01(reloadTimer / reloadTime));
            ui.SetEmpty(ammo == 0 && !reloading && gunReady);
            ui.SetWeaponOut(!holstered);
            ui.SetCrosshair(currentSpread, cam.fieldOfView, aiming, showCrosshair && !reloading && gunReady);
        }
    }

    void LateUpdate()
    {
        // Gun kicks back and up, then springs back (after the animation has run)
        if (gunModel == null || cam == null) return;

        gunModel.localPosition = gunRestPos;
        gunModel.localRotation = gunRestRot;

        // Put the arms (and the gun in the hand) away by shrinking them out of view, first person only
        bool firstPerson = head == null || Vector3.Distance(cam.transform.position, head.position) < 1f;
        float armScale = firstPerson ? 1f - away : 1f;
        if (leftArm != null) leftArm.localScale = Vector3.one * armScale;
        if (rightArm != null) rightArm.localScale = Vector3.one * armScale;
        if (rightArm == null) gunModel.gameObject.SetActive(away < 0.99f); // no arm bones: just hide the gun
        if (kick < 0.001f) return;

        Vector3 back = -cam.transform.forward * kickBack * kick;
        gunModel.position += back;
        gunModel.rotation = Quaternion.AngleAxis(-kickUp * kick, cam.transform.right) * gunModel.rotation;
    }

    // ---------- Shooting ----------

    void Shoot()
    {
        ammo--;
        nextShotTime = Time.time + fireRate;
        bloom += shotBloom * (aiming ? 0.4f : 1f);
        kick = 1f;

        if (playAnimations && anim != null) anim.SetTrigger(shootTrigger);
        Play(shootSound, Random.Range(0.95f, 1.05f));

        Vector3 muzzlePos = MuzzlePosition();

        // Flash
        flashLight.transform.position = muzzlePos;
        flashTimer = 0.06f;
        if (flashFx != null)
        {
            flashFx.transform.position = muzzlePos;
            flashFx.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
            flashFx.Emit(6);
        }

        // Where the bullet goes: center of the screen, with a little spread
        Vector2 r = Random.insideUnitCircle * Mathf.Tan(currentSpread * Mathf.Deg2Rad);
        Vector3 dir = (cam.transform.forward + cam.transform.right * r.x + cam.transform.up * r.y).normalized;
        Ray ray = new Ray(cam.transform.position, dir);

        // The world
        float worldDist = range;
        RaycastHit worldHit = new RaycastHit();
        bool hitWorld = false;
        foreach (RaycastHit h in Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.transform.IsChildOf(transform.root)) continue; // don't shoot ourselves
            if (h.distance < worldDist)
            {
                worldDist = h.distance;
                worldHit = h;
                hitWorld = true;
            }
        }

        // The ghost has no collider, so check her shape directly.
        // Only her heart can be hurt. Bullets pass through her smoke body harmlessly.
        ShadowPerson heartHit = null;
        float heartDist = worldDist;
        ShadowPerson bodyHit = null;
        float bodyDist = worldDist;

        foreach (ShadowPerson g in FindObjectsOfType<ShadowPerson>())
        {
            if (!g.CanBeHit) continue;
            float d;

            GhostSmokeBody smoke = g.GetComponent<GhostSmokeBody>();
            Vector3 heart = smoke != null ? smoke.HeartPosition : g.transform.position + Vector3.up * 1.3f;
            // Her heart only takes damage while it's open (when she attacks or rests)
            if (g.HeartOpen && HitsSphere(ray, heart, heartHitSize, out d) && d < heartDist)
            {
                heartHit = g;
                heartDist = d;
            }

            if (HitsBody(ray, g.transform.position, 0.35f, 1.8f, out d) && d < bodyDist)
            {
                bodyHit = g;
                bodyDist = d;
            }
        }

        // In the final moment, hitting her anywhere counts
        if (bodyHit != null && heartHit == null && bodyHit.InFinalMoment)
        {
            heartHit = bodyHit;
            heartDist = bodyDist;
        }

        // Shot through her smoke: it swirls, but nothing happens to her
        if (bodyHit != null && heartHit == null)
            Burst(ghostPuff, ray.GetPoint(bodyDist), -dir, 8);

        Vector3 end;
        if (heartHit != null)
        {
            end = ray.GetPoint(heartDist);
            heartHit.TakeHit();
            hitMarkerTimer = 0.25f;
            if (UIManager.Instance != null) UIManager.Instance.ShowHitMarker();
            Play(hitSound, Random.Range(0.9f, 1.1f));
            Burst(ghostPuff, end, -dir, 25);
        }
        else if (hitWorld)
        {
            end = worldHit.point;
            Burst(sparks, end, worldHit.normal, 10);
            Burst(dust, end, worldHit.normal, 6);
            if (worldHit.rigidbody != null) worldHit.rigidbody.AddForceAtPosition(dir * hitForce, end, ForceMode.Impulse);
        }
        else
        {
            end = ray.GetPoint(range);
        }

        if (showTracer && tracer != null)
        {
            tracer.SetPosition(0, muzzlePos);
            tracer.SetPosition(1, end);
            tracerTimer = 0.05f;
        }
    }

    void CancelReload()
    {
        reloading = false;
        if (playAnimations && anim != null) anim.ResetTrigger(reloadTrigger);
    }

    void StartReload()
    {
        if (reloading || ammo >= magazineSize) return;
        reloading = true;
        reloadTimer = reloadTime;
        if (playAnimations && anim != null) anim.SetTrigger(reloadTrigger);
        Play(reloadSound, 1f);
    }

    // Does the ray go through a ball (her heart)?
    bool HitsSphere(Ray ray, Vector3 center, float radius, out float dist)
    {
        dist = 0f;
        Vector3 v = center - ray.origin;
        float t = Vector3.Dot(v, ray.direction);
        if (t < 0f) return false;
        float d2 = v.sqrMagnitude - t * t;
        float r2 = radius * radius;
        if (d2 > r2) return false;
        dist = t - Mathf.Sqrt(r2 - d2);
        return true;
    }

    // Does the ray go through a standing body (an upright cylinder)?
    bool HitsBody(Ray ray, Vector3 feet, float radius, float height, out float dist)
    {
        dist = 0f;
        Vector3 o = ray.origin - feet;
        Vector3 d = ray.direction;

        float a = d.x * d.x + d.z * d.z;
        if (a < 0.000001f) return false;
        float b = 2f * (o.x * d.x + o.z * d.z);
        float c = o.x * o.x + o.z * o.z - radius * radius;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) return false;

        float sq = Mathf.Sqrt(disc);
        float t = (-b - sq) / (2f * a);
        if (t < 0f) t = (-b + sq) / (2f * a);
        if (t < 0f) return false;

        float y = o.y + d.y * t;
        if (y < 0f || y > height) return false;

        dist = t;
        return true;
    }

    Vector3 MuzzlePosition()
    {
        if (muzzle != null) return muzzle.position;

        if (gunModel != null)
        {
            Renderer rend = gunModel.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Bounds bb = rend.bounds;
                return bb.center + cam.transform.forward * bb.extents.magnitude * 0.8f;
            }
        }

        Transform c = cam.transform;
        return c.position + c.forward * 0.6f + c.right * 0.15f - c.up * 0.15f;
    }

    // ---------- Effects ----------

    void BuildEffects()
    {
        GameObject lightGo = new GameObject("Muzzle Flash Light");
        flashLight = lightGo.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.range = 5f;
        flashLight.color = flashColor;
        flashLight.intensity = 0f;
        flashLight.shadows = LightShadows.None;

        if (showTracer)
        {
            GameObject t = new GameObject("Bullet Tracer");
            tracer = t.AddComponent<LineRenderer>();
            tracer.positionCount = 2;
            tracer.startWidth = 0.012f;
            tracer.endWidth = 0.004f;
            Shader lineShader = Shader.Find("Sprites/Default");
            if (lineShader == null) lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            tracer.material = new Material(lineShader);
            tracer.shadowCastingMode = ShadowCastingMode.Off;
            tracer.receiveShadows = false;
            tracer.enabled = false;
        }

        if (smokeMaterial == null)
        {
            Debug.LogWarning("PlayerGun: drag GhostFogMat into 'Smoke Material' for muzzle flash and impact smoke.");
            return;
        }

        // Bright HDR copy of the smoke texture for the flash and sparks (glows with Bloom)
        Material glow = new Material(smokeMaterial);
        Color hdr = flashColor * 4f;
        hdr.a = 1f;
        if (glow.HasProperty("_BaseColor")) glow.SetColor("_BaseColor", hdr);
        if (glow.HasProperty("_Color")) glow.SetColor("_Color", hdr);

        flashFx = MakeFx("Muzzle Flash", glow, 0.04f, 0.07f, 0.03f, 0.08f, 0f, 1f, Color.white, false);
        var fs = flashFx.shape; fs.angle = 15f;

        sparks = MakeFx("Impact Sparks", glow, 0.15f, 0.35f, 0.01f, 0.025f, 2f, 5f, Color.white, true);
        var sg = sparks.main; sg.gravityModifier = 1.5f;

        dust = MakeFx("Impact Dust", smokeMaterial, 0.8f, 1.5f, 0.08f, 0.2f, 0.2f, 0.8f, new Color(0.5f, 0.48f, 0.45f, 0.4f), false);

        ghostPuff = MakeFx("Ghost Hit Smoke", smokeMaterial, 0.8f, 1.6f, 0.15f, 0.35f, 0.5f, 2f, new Color(0f, 0f, 0f, 0.6f), false);
    }

    ParticleSystem MakeFx(string label, Material mat, float lifeMin, float lifeMax, float sizeMin, float sizeMax,
                          float speedMin, float speedMax, Color color, bool stretch)
    {
        GameObject go = new GameObject(label);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.01f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, stretch ? 1f : 0.6f), new Keyframe(1f, stretch ? 0.3f : 1.8f)));

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        if (stretch)
        {
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.04f;
            rend.lengthScale = 1f;
        }

        ps.Play();
        return ps;
    }

    void Burst(ParticleSystem ps, Vector3 pos, Vector3 normal, int count)
    {
        if (ps == null) return;
        ps.transform.position = pos + normal * 0.02f;
        ps.transform.rotation = Quaternion.LookRotation(normal);
        ps.Emit(count);
    }

    void Play(AudioClip clip, float pitch)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }

    Transform FindChild(Transform root, string part)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains(part)) return t;
        return null;
    }

    // ---------- Crosshair, hit marker, ammo ----------

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        // The Canvas UI takes over when there's a UIManager; this is only the backup
        if (UIManager.Instance != null) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float scale = Screen.height / 1080f;

        if (showCrosshair && !reloading && away < 0.05f)
        {
            // Gap grows with spread, so the crosshair shows how accurate you are
            float pxPerDeg = Screen.height / Mathf.Max(1f, cam != null ? cam.fieldOfView : 60f);
            float gap = (4f * scale) + currentSpread * pxPerDeg;
            float len = 9f * scale;
            float thick = Mathf.Max(2f, 2f * scale);
            Color c = crosshairColor;
            if (aiming) c.a *= 0.6f;

            Line(cx - thick / 2f, cy - gap - len, thick, len, c);   // top
            Line(cx - thick / 2f, cy + gap, thick, len, c);         // bottom
            Line(cx - gap - len, cy - thick / 2f, len, thick, c);   // left
            Line(cx + gap, cy - thick / 2f, len, thick, c);         // right
            Line(cx - thick / 2f, cy - thick / 2f, thick, thick, c); // dot
        }

        // Hit marker: a white X when you hit her
        if (hitMarkerTimer > 0f)
        {
            Color hc = new Color(1f, 1f, 1f, Mathf.Clamp01(hitMarkerTimer / 0.25f));
            float len = 10f * scale;
            float off = 7f * scale;
            float thick = Mathf.Max(2f, 2f * scale);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            Line(cx - thick / 2f, cy - off - len, thick, len, hc);
            Line(cx - thick / 2f, cy + off, thick, len, hc);
            Line(cx - off - len, cy - thick / 2f, len, thick, hc);
            Line(cx + off, cy - thick / 2f, len, thick, hc);
            GUI.matrix = m;
        }

        if (showAmmo) DrawAmmo(scale);
    }

    void DrawAmmo(float scale)
    {
        float margin = 50f * scale;
        float right = Screen.width - margin;
        float bottom = Screen.height - margin;

        GUIStyle big = TextStyle(Mathf.RoundToInt(60f * scale), FontStyle.Bold);
        GUIStyle small = TextStyle(Mathf.RoundToInt(24f * scale), FontStyle.Normal);

        bool low = ammo <= Mathf.Max(1, magazineSize / 4);
        Color mainColor = ammo == 0 ? new Color(1f, 0.3f, 0.25f, 0.95f)
                        : low ? new Color(1f, 0.75f, 0.4f, 0.95f)
                        : new Color(1f, 1f, 1f, 0.95f);

        // Measure the text so nothing overlaps or gets cut off
        string bigText = ammo.ToString();
        string smallText = "/ " + magazineSize;
        Vector2 bigSize = big.CalcSize(new GUIContent(bigText));
        Vector2 smallSize = small.CalcSize(new GUIContent(smallText));

        // Bullet row along the bottom, right-aligned
        float pipH = 10f * scale;
        float pipGap = 3f * scale;
        float maxRow = Mathf.Max(bigSize.x + smallSize.x, 120f * scale);
        float pipW = Mathf.Clamp((maxRow - pipGap * (magazineSize - 1)) / Mathf.Max(1, magazineSize), 2f, 5f * scale);
        float rowW = magazineSize * pipW + (magazineSize - 1) * pipGap;
        float pipY = bottom - pipH;
        for (int i = 0; i < magazineSize; i++)
        {
            // Bullets empty from the right, like a real magazine
            Color pc = i < ammo ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.15f);
            Line(right - rowW + i * (pipW + pipGap), pipY, pipW, pipH, pc);
        }

        // "12" big, then "/ 12" small, sitting on top of the bullet row
        float textBottom = pipY - 6f * scale;
        Rect smallRect = new Rect(right - smallSize.x, textBottom - smallSize.y - bigSize.y * 0.12f, smallSize.x, smallSize.y);
        Rect bigRect = new Rect(smallRect.x - 8f * scale - bigSize.x, textBottom - bigSize.y, bigSize.x, bigSize.y);

        Shadowed(bigRect, bigText, big, mainColor);
        Shadowed(smallRect, smallText, small, new Color(1f, 1f, 1f, 0.5f));

        GUIStyle label = TextStyle(Mathf.RoundToInt(16f * scale), FontStyle.Bold);
        label.alignment = TextAnchor.MiddleCenter;

        if (reloading)
        {
            // Reload bar under the crosshair
            float w = 160f * scale, h = 3f * scale;
            float x = Screen.width * 0.5f - w / 2f, y = Screen.height * 0.5f + 40f * scale;
            float k = 1f - Mathf.Clamp01(reloadTimer / reloadTime);
            Line(x, y, w, h, new Color(1f, 1f, 1f, 0.2f));
            Line(x, y, w * k, h, new Color(1f, 1f, 1f, 0.85f));
            Shadowed(new Rect(x, y - 28f * scale, w, 22f * scale), "RELOADING", label, new Color(1f, 1f, 1f, 0.7f));
        }
        else if (ammo == 0)
        {
            // Blinks when the gun is empty
            float a = 0.4f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 4f)) + emptyBlink * 0.3f;
            Shadowed(new Rect(Screen.width * 0.5f - 100f * scale, Screen.height * 0.5f + 30f * scale, 200f * scale, 24f * scale),
                     "RELOAD  [R]", label, new Color(1f, 0.35f, 0.3f, Mathf.Clamp01(a)));
        }
    }

    GUIStyle TextStyle(int size, FontStyle fontStyle)
    {
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = Mathf.Max(8, size);
        st.fontStyle = fontStyle;
        st.wordWrap = false;
        st.clipping = TextClipping.Overflow;
        st.alignment = TextAnchor.LowerRight;
        st.padding = new RectOffset(0, 0, 0, 0);
        st.margin = new RectOffset(0, 0, 0, 0);
        return st;
    }

    void Line(float x, float y, float w, float h, Color c)
    {
        // Thin dark outline so it's readable on any background
        GUI.color = new Color(0f, 0f, 0f, c.a * 0.5f);
        GUI.DrawTexture(new Rect(x - 1f, y - 1f, w + 2f, h + 2f), pixel);
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), pixel);
        GUI.color = Color.white;
    }

    void Shadowed(Rect r, string text, GUIStyle style, Color c)
    {
        style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.6f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }
}