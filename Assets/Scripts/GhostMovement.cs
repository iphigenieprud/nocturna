using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// THE SHADOW WOMAN - how she works (one clear set of rules)
//
// She appears when you light your first candle. Then she cycles through:
//   HAUNT     She keeps her distance, watching you. Every few seconds she fades and reappears
//             somewhere around you (usually where you can see her). She never touches you here,
//             and if you walk right up to her, she fades away.
//   CANDLE    When a candle has been burning for a while, she hops toward it, then BLOWS it out
//             (her heart is open while she blows: shoot it to save the candle) and DRINKS its smoke.
//   ATTACK    Every so often: she appears in front of you, her heart pulses (warning, heart open),
//             then she LUNGES in a straight line. Only the lunge can hurt you. Hit or miss, she pulls back.
//   STAGGER   Shot in her open heart: she flinches, stops what she was doing, then pulls back.
//   REST      Now and then she goes to the blooming tree and rests (heart open, harmless), growing paler.
//
// Darker = faster. She gets darker from candle smoke and from being shot; the tree makes her paler.
// When every memory is found she's at her angriest (fully dark, no more resting) until the final moment.
public class ShadowPerson : MonoBehaviour
{
    [Tooltip("Leave empty to find the player automatically.")]
    public Transform player;

    [Header("Movement")]
    [Tooltip("How fast she walks when she drifts.")]
    public float walkSpeed = 1f;
    [Tooltip("While haunting, she stays this far from you (min, max meters).")]
    public Vector2 hauntDistance = new Vector2(8f, 18f);
    [Tooltip("While haunting, seconds between her reappearances (min, max).")]
    public Vector2 hauntInterval = new Vector2(4f, 8f);

    [Header("Attacks")]
    [Tooltip("Seconds between her attacks (min, max).")]
    public Vector2 attackCooldown = new Vector2(12f, 20f);
    [Tooltip("How fast she lunges when calm (meters per second). Darker = faster.")]
    public float lungeSpeed = 4.5f;

    [Header("Candles")]
    [Tooltip("Seconds it takes her to blow a candle out.")]
    public float blowTime = 3f;
    [Tooltip("Seconds a newly lit candle is left alone before she goes for it.")]
    public float candleGrace = 25f;

    [Header("Anger")]
    [Tooltip("At full darkness she's this much faster at everything. 1.2 = 2.2 times as fast.")]
    public float angerSpeedup = 1.2f;
    [Tooltip("How much darker she gets each time you shoot her heart.")]
    [Range(0f, 1f)] public float darknessPerHit = 0.15f;

    [Header("Fighting")]
    [Tooltip("Heart shots it takes to destroy her.")]
    public int health = 3;
    [Tooltip("How close her lunge has to get to hurt you.")]
    public float catchDistance = 1.2f;

    [Header("Ground")]
    [Tooltip("What counts as ground. If she ends up standing on trees, untick their layer here.")]
    public LayerMask groundLayers = ~0;

    // ---- Fixed settings (kept out of the Inspector) ----
    const float FirstAppearDelay = 2f;
    const float FirstAppearDistance = 15f;
    const float VanishTime = 0.7f;
    const float BehindChance = 0.2f;          // how often a haunt reappearance is behind you
    const float TooClose = 3f;                // walk this close while she haunts and she fades away
    const float BlowDistance = 1.1f;
    const float DrinkTime = 4f;
    const float LeanAngle = 20f;
    const float TurnSpeed = 4f;
    const float AttackAppearDistance = 7f;
    const float WindupTime = 0.6f;            // the warning before every lunge
    const float LungeTime = 1.6f;             // how long a lunge lasts before she gives up
    const float StaggerTime = 1.2f;
    const float RestTime = 8f;
    static readonly Vector2 RestEvery = new Vector2(25f, 45f);
    static readonly Vector2 CandleHop = new Vector2(6f, 10f);       // meters per hop toward a candle
    static readonly Vector2 CandleHopTime = new Vector2(1.5f, 3f);  // seconds between hops

    enum Mode { Haunt, GoToCandle, Blowing, Drinking, Attack, Stagger, Resting }

    // The final moment: she vanishes, lunges from right in front of you; if you don't shoot, she goes to the tree
    enum Final { None, Charge, Pause, WalkToTree, WaitAtTree, Merging, Done }

    Camera cam;
    Animator anim;
    GhostSmokeBody smoke;
    bool hasSpeedParam;

    Mode mode = Mode.Haunt;
    Mode lastMode = Mode.Haunt;
    Final final = Final.None;

    Candle targetCandle;
    float timer;            // counts down to her next decision (haunt step, candle hop, rest end...)
    float windup;           // > 0 while her heart pulses before a lunge
    float attackReady;      // counts down to her next attack
    float restTimer;        // counts down to her next rest
    float currentSpeed;
    float currentLean;
    int maxHealth;
    float healProgress;

    bool vanished;
    bool waitingForCandle = true;
    bool hiddenAtStart;
    bool dead;
    Vector3 nextPosition;
    Mode appearAs = Mode.Haunt;   // what she does when she reappears

    bool finalNext;
    float finalTimer;
    Vector3 mergeFrom, mergeScale;
    bool mergeBloomed;
    bool wasEnraged;
    bool shotAtTheEnd;

    // Candles the player lit, oldest first, and when
    readonly List<Candle> litByPlayer = new List<Candle>();
    readonly Dictionary<Candle, float> litTime = new Dictionary<Candle, float>();

    // ---------- What other scripts can ask ----------

    // Visible and can be shot
    public bool CanBeHit { get { return enabled && !dead && !vanished && !waitingForCandle; } }

    // Her heart can only be hurt while open: the warning pulse and lunge, blowing a candle, resting, the final moment
    public bool HeartOpen
    {
        get
        {
            return CanBeHit && (final != Final.None || windup > 0f || mode == Mode.Attack
                                || mode == Mode.Blowing || mode == Mode.Resting);
        }
    }

    // In the final moment (a hit anywhere on her counts)
    public bool InFinalMoment { get { return final != Final.None && final != Final.Done; } }

    // Every memory found: at her angriest until the final moment
    bool Enraged { get { return Memento.AllCollected && final == Final.None; } }

    // 1 = calm. Darker = faster at everything.
    float Speed
    {
        get
        {
            float d = Enraged ? 1f : (smoke != null ? smoke.Darkness : 0f);
            return 1f + angerSpeedup * d;
        }
    }

    // ---------- Setup ----------

    void OnEnable() { Candle.AnyLit += OnCandleLit; }
    void OnDisable() { Candle.AnyLit -= OnCandleLit; }

    void OnCandleLit(Candle c)
    {
        if (Time.timeSinceLevelLoad < 0.5f) return;   // candles lit at the start don't count
        litByPlayer.Remove(c);
        litByPlayer.Add(c);
        litTime[c] = Time.time;
    }

    void Start()
    {
        cam = Camera.main;
        anim = GetComponentInChildren<Animator>();
        smoke = GetComponent<GhostSmokeBody>();
        maxHealth = health;
        attackReady = attackCooldown.x;   // no attack right when she first shows up
        restTimer = Random.Range(RestEvery.x, RestEvery.y);

        if (player == null) player = FindPlayer();
        if (player == null) Debug.LogWarning("ShadowPerson: no player found. Drag your player into the Player slot.");

        if (anim != null)
        {
            foreach (AnimatorControllerParameter p in anim.parameters)
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) hasSpeedParam = true;

            // Root motion would hold her in place: the script does the moving
            if (anim.applyRootMotion)
            {
                anim.applyRootMotion = false;
                Debug.Log("ShadowPerson: turned off 'Apply Root Motion' on her Animator so she can move.");
            }
        }

        // Physics or navigation components would fight the script and pin her in place
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) rb.isKinematic = true;
        foreach (Behaviour b in GetComponentsInChildren<Behaviour>())
            if (b != null && b.GetType().Name == "NavMeshAgent") b.enabled = false;
        foreach (CharacterController cc in GetComponentsInChildren<CharacterController>()) cc.enabled = false;

        transform.position = OnGround(transform.position);

        if (PlayerHealth.Instance == null)
            Debug.LogWarning("ShadowPerson: there's no PlayerHealth in the scene, so getting caught will restart the level.");

        vanished = true;   // hidden until the first candle
    }

    // ---------- Every frame ----------

    void Update()
    {
        if (dead) return;

        if (waitingForCandle && !hiddenAtStart)
        {
            hiddenAtStart = true;
            if (smoke != null) smoke.Vanish(true);
        }

        if (player == null) player = FindPlayer();
        if (player == null) return;

        float dt = Time.deltaTime;
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distToPlayer = toPlayer.magnitude;

        // Not in the game yet: she arrives behind you after your first candle
        if (waitingForCandle)
        {
            if (litByPlayer.Count > 0)
            {
                waitingForCandle = false;
                appearAs = Mode.Haunt;
                nextPosition = PointBehindPlayer(FirstAppearDistance);
                timer = FirstAppearDelay;
            }
            return;
        }

        // Gone for a moment: reappear when the timer runs out
        if (vanished)
        {
            if (final == Final.Done) return;
            timer -= dt;
            if (timer <= 0f) Appear();
            return;
        }

        if (final != Final.None)
        {
            UpdateFinal(dt, toPlayer, distToPlayer);
            return;
        }

        // The last memory was taken: her angriest
        if (Enraged && !wasEnraged)
        {
            wasEnraged = true;
            if (smoke != null) { smoke.FlareHeart(1f); smoke.Flinch(); }
            if (mode == Mode.Resting) GoHaunt(0.5f);
            Debug.Log("ShadowPerson: every memory is found. She's at her angriest.");
        }
        if (Enraged && smoke != null && smoke.Darkness < 1f) smoke.AddDarkness(1f);

        // Timers that run in the background
        if (mode != Mode.Resting) attackReady -= dt;
        if (mode == Mode.Haunt || mode == Mode.GoToCandle) restTimer -= dt;

        // Her candle went out some other way
        if ((mode == Mode.GoToCandle || mode == Mode.Blowing) && (targetCandle == null || !targetCandle.IsLit))
            GoHaunt(0.5f);

        // A candle has burned long enough: she goes for it (the newest one)
        if (mode == Mode.Haunt || mode == Mode.GoToCandle)
        {
            Candle c = CandleToTake();
            if (c != null && c != targetCandle)
            {
                targetCandle = c;
                if (mode == Mode.Haunt) timer = Mathf.Min(timer, 0.5f);
                mode = Mode.GoToCandle;
            }
        }

        Vector3 faceDir = toPlayer;
        float targetSpeed = 0f;

        switch (mode)
        {
            case Mode.Haunt:
                // Drifts closer if you're far, otherwise stands and watches
                if (distToPlayer > hauntDistance.y) targetSpeed = walkSpeed * Speed;

                // Walk right up to her and she slips away
                if (distToPlayer < TooClose) { FadeAway(); return; }

                timer -= dt;
                if (distToPlayer > hauntDistance.y + 12f) timer = 0f;   // you got far away: she follows
                if (timer <= 0f) { Decide(); return; }
                break;

            case Mode.GoToCandle:
                {
                    Vector3 toCandle = Flat(targetCandle.transform.position - transform.position);
                    faceDir = toCandle;
                    if (toCandle.magnitude <= BlowDistance + 0.3f) { StartBlowing(); break; }

                    targetSpeed = walkSpeed * Speed;
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        if (AttackReady) StartAttack();
                        else HopTo(PointTowardCandle(toCandle), Mode.GoToCandle);
                        return;
                    }
                    break;
                }

            case Mode.Blowing:
                {
                    faceDir = Flat(targetCandle.transform.position - transform.position);
                    timer += dt;
                    float needed = blowTime / Speed;
                    targetCandle.SetBlowProgress(timer / needed);
                    if (timer >= needed)
                    {
                        targetCandle.BlowOut();
                        mode = Mode.Drinking;
                        timer = DrinkTime;
                    }
                    break;
                }

            case Mode.Drinking:
                if (targetCandle != null) faceDir = Flat(targetCandle.transform.position - transform.position);
                timer -= dt;
                if (timer <= 0f) GoHaunt(Random.Range(1f, 2f));
                break;

            case Mode.Attack:
                if (windup > 0f) windup -= dt;   // heart pulses: the warning
                else
                {
                    targetSpeed = lungeSpeed * Speed;
                    timer -= dt;
                    if (timer <= 0f) { FadeAway(); return; }   // missed: she pulls back
                }
                break;

            case Mode.Stagger:
                timer -= dt;
                if (timer <= 0f) { FadeAway(); return; }
                break;

            case Mode.Resting:
                if (BloomTree.Instance != null) faceDir = Flat(BloomTree.Instance.transform.position - transform.position);
                timer -= dt;
                if (timer <= 0f) GoHaunt(Random.Range(1f, 2f));
                break;
        }

        if (mode != lastMode)
        {
            Debug.Log("ShadowPerson: " + lastMode + " -> " + mode);
            lastMode = mode;
        }

        Move(faceDir, targetSpeed, dt);

        // Only her lunge can hurt you
        if (mode == Mode.Attack && windup <= 0f && distToPlayer < catchDistance) Catch();
    }

    // At each decision point while haunting: rest, attack, or reappear somewhere around you
    void Decide()
    {
        if (WantsRest()) GoRest();
        else if (AttackReady) StartAttack();
        else HopTo(PointAroundPlayer(), Mode.Haunt);
    }

    void Move(Vector3 faceDir, float targetSpeed, float dt)
    {
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, (mode == Mode.Attack ? 10f : 4f) * dt);
        if (faceDir.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = faceDir.normalized;
            transform.position += dir * currentSpeed * dt;

            float leanTarget = windup > 0f ? -12f                       // rears back before the lunge
                             : mode == Mode.Attack ? 18f                // leaning into the lunge
                             : mode == Mode.Blowing ? LeanAngle : 0f;
            currentLean = Mathf.Lerp(currentLean, leanTarget, 3f * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir) * Quaternion.Euler(currentLean, 0f, 0f), TurnSpeed * dt);
        }
        transform.position = OnGround(transform.position);
        if (hasSpeedParam) anim.SetFloat("Speed", currentSpeed);
    }

    // ---------- States ----------

    void GoHaunt(float firstStep)
    {
        if (mode == Mode.Blowing && targetCandle != null && targetCandle.IsLit) targetCandle.CancelBlow();
        targetCandle = null;
        windup = 0f;
        mode = Mode.Haunt;
        timer = firstStep;
    }

    bool AttackReady { get { return attackReady <= 0f; } }

    void ResetAttackCooldown()
    {
        attackReady = Random.Range(attackCooldown.x, attackCooldown.y) / Mathf.Lerp(1f, Speed, 0.3f);
    }

    // Appears in front of you (where you can see her), heart pulses, then she lunges
    void StartAttack()
    {
        ResetAttackCooldown();
        Vector3 dir = cam != null ? cam.transform.forward : player.forward;
        dir = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * Flat(dir).normalized;
        HopTo(player.position + dir * AttackAppearDistance, Mode.Attack);
    }

    void StartBlowing()
    {
        mode = Mode.Blowing;
        timer = 0f;
        currentSpeed = 0f;
        targetCandle.StartBlow(transform);
        if (SoundManager.Instance != null) SoundManager.Instance.GhostBlow(transform.position);
    }

    bool WantsRest()
    {
        return !Enraged && restTimer <= 0f && BloomTree.Instance != null && BloomTree.Instance.CanHeal;
    }

    void GoRest()
    {
        restTimer = Random.Range(RestEvery.x, RestEvery.y);
        HopTo(BloomTree.Instance.RestSpot(player.position), Mode.Resting);
    }

    // ---------- Fading and appearing ----------

    // Fade out now, reappear at 'position' doing 'then'
    void HopTo(Vector3 position, Mode then)
    {
        appearAs = then;
        FadeTo(position);
    }

    void FadeTo(Vector3 position)
    {
        if (mode == Mode.Blowing && targetCandle != null && targetCandle.IsLit) targetCandle.CancelBlow();
        vanished = true;
        windup = 0f;
        nextPosition = position;
        timer = VanishTime / Speed;
        currentSpeed = 0f;
        if (smoke != null) smoke.Vanish();
        if (SoundManager.Instance != null && !waitingForCandle) SoundManager.Instance.GhostVanish(transform.position);
    }

    // Pull back to a safe distance and go back to haunting
    void FadeAway()
    {
        targetCandle = null;
        HopTo(PointAroundPlayer(), Mode.Haunt);
        timer = VanishTime * 2f / Speed;
    }

    void Appear()
    {
        vanished = false;
        transform.position = OnGround(nextPosition);

        if (finalNext)
        {
            finalNext = false;
            final = Final.Charge;
            transform.position = OnGround(FrontOfPlayer(FinalAppearDistance));
            windup = FinalWindup;
            currentSpeed = 0f;
            if (smoke != null) { smoke.Appear(); smoke.FlareHeart(FinalWindup); }
            if (SoundManager.Instance != null) SoundManager.Instance.GhostWarning(transform.position);
            FacePlayer();
            return;
        }

        mode = appearAs;
        currentSpeed = 0f;

        switch (mode)
        {
            case Mode.Attack:
                windup = WindupTime;
                timer = LungeTime;
                if (smoke != null) smoke.FlareHeart(WindupTime);
                if (SoundManager.Instance != null) SoundManager.Instance.GhostWarning(transform.position);
                FacePlayer();
                break;
            case Mode.Resting:
                timer = RestTime;
                if (BloomTree.Instance != null) FaceTowards(BloomTree.Instance.transform.position);
                break;
            case Mode.GoToCandle:
                timer = Random.Range(CandleHopTime.x, CandleHopTime.y) / Speed;
                if (targetCandle != null) FaceTowards(targetCandle.transform.position);
                break;
            default:
                mode = Mode.Haunt;
                timer = Random.Range(hauntInterval.x, hauntInterval.y) / Speed;
                FacePlayer();
                break;
        }

        if (smoke != null) smoke.Appear();
        if (SoundManager.Instance != null && mode != Mode.Attack) SoundManager.Instance.GhostAppear(transform.position);
    }

    void FacePlayer() { FaceTowards(player.position); }

    void FaceTowards(Vector3 p)
    {
        Vector3 look = Flat(p - transform.position);
        if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
    }

    // ---------- Where she appears ----------

    // Somewhere around you at haunting distance: usually where you can see her, sometimes behind you
    Vector3 PointAroundPlayer()
    {
        Vector3 forward = Flat(cam != null ? cam.transform.forward : player.forward).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 dir = Random.value < BehindChance ? -forward : forward;
        dir = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * dir;
        return player.position + dir * Random.Range(hauntDistance.x, hauntDistance.y);
    }

    Vector3 PointBehindPlayer(float dist)
    {
        Vector3 back = Flat(cam != null ? -cam.transform.forward : -player.forward).normalized;
        if (back.sqrMagnitude < 0.01f) back = -Vector3.forward;
        back = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * back;
        return player.position + back * dist;
    }

    // Closer to the candle; right beside it on the last hop
    Vector3 PointTowardCandle(Vector3 toCandle)
    {
        float dist = toCandle.magnitude;
        Vector3 dir = toCandle / Mathf.Max(dist, 0.001f);
        float step = Random.Range(CandleHop.x, CandleHop.y) * Mathf.Sqrt(Speed);
        if (dist - step <= BlowDistance + 0.5f) return targetCandle.transform.position - dir * BlowDistance;
        Vector3 side = Vector3.Cross(Vector3.up, dir) * Random.Range(-1.5f, 1.5f);
        return transform.position + dir * step + side;
    }

    Vector3 FrontOfPlayer(float distance)
    {
        Vector3 dir = Flat(cam != null ? cam.transform.forward : player.forward);
        if (dir.sqrMagnitude < 0.01f) dir = player.forward;
        return player.position + dir.normalized * distance;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ---------- Candles ----------

    // The newest candle you lit that has burned past its grace period
    Candle CandleToTake()
    {
        for (int i = litByPlayer.Count - 1; i >= 0; i--)
        {
            Candle c = litByPlayer[i];
            if (c == null || !c.IsLit) { litByPlayer.RemoveAt(i); continue; }
            if (c == targetCandle) return c;
            float t;
            if (litTime.TryGetValue(c, out t) && Time.time - t < candleGrace) continue;
            if (!c.BeingBlown) return c;
        }
        return null;
    }

    // ---------- The tree ----------

    // Called by the tree while she's near it: she grows paler and slowly heals
    public bool TreeHeal(float dt, float strength, float calmPerSecond, float secondsPerHeal)
    {
        if (!CanBeHit || Enraged) return false;
        bool soothing = (smoke != null && smoke.Darkness > 0.001f) || health < maxHealth;
        if (smoke != null) smoke.AddDarkness(-calmPerSecond * strength * dt);
        if (health < maxHealth)
        {
            healProgress += dt * strength / Mathf.Max(0.1f, secondsPerHeal);
            if (healProgress >= 1f) { healProgress = 0f; health++; }
        }
        return soothing;
    }

    // ---------- Hurting you, being hurt ----------

    void Catch()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }
        if (!ph.TakeHit()) return;   // you were still safe from the last hit
        FadeAway();
    }

    // Called by the gun when her open heart is hit (or anywhere on her in the final moment)
    public void TakeHit()
    {
        if (!CanBeHit || !HeartOpen) return;

        if (InFinalMoment)
        {
            final = Final.None;   // one life left: this ends it
            shotAtTheEnd = true;
        }

        health--;
        if (smoke != null) smoke.AddDarkness(darknessPerHit);   // hurting her makes her darker and faster

        if (health <= 0) { Die(); return; }

        // Stagger: whatever she was doing stops (a candle she was blowing is saved), then she pulls back
        if (mode == Mode.Blowing && targetCandle != null && targetCandle.IsLit) targetCandle.CancelBlow();
        targetCandle = null;
        windup = 0f;
        mode = Mode.Stagger;
        timer = StaggerTime;
        currentSpeed = 0f;
        if (smoke != null) smoke.Flinch();
    }

    void Die()
    {
        dead = true;
        if (targetCandle != null && targetCandle.IsLit) targetCandle.CancelBlow();
        if (smoke != null) smoke.StopSmoke();
        if (SoundManager.Instance != null) SoundManager.Instance.GhostDeath(transform.position);
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (EndingManager.Instance != null)
        {
            if (shotAtTheEnd) EndingManager.Instance.SheDiedAtTheEnd();   // her own ending
            else EndingManager.Instance.SheDied();
        }
        Destroy(gameObject, 4f);
    }

    // ---------- The final moment ----------

    // Fixed timings (on purpose not in the Inspector)
    const float FinalGoneTime = 4f;        // she disappears this long before the last lunge
    const float FinalAppearDistance = 7f;  // she reappears this far right in front of you
    const float FinalWindup = 0.35f;       // a short pulse, then she lunges
    const float FinalFaceTime = 2.5f;      // face to face before she turns away
    const float FinalTogetherTime = 3f;    // seconds you heal beside her at the tree before you merge
    const float MergeTime = 5f;            // seconds she takes to sink into the tree

    // Called by EndingManager when every memory is found and every candle is lit
    public bool StartFinalTest()
    {
        if (dead || player == null || BloomTree.Instance == null) return false;

        if (mode == Mode.Blowing && targetCandle != null && targetCandle.IsLit) targetCandle.CancelBlow();
        targetCandle = null;
        mode = Mode.Haunt;
        waitingForCandle = false;
        health = 1;          // one shot ends it

        finalNext = true;
        FadeTo(transform.position);
        timer = FinalGoneTime;
        return true;
    }

    void UpdateFinal(float dt, Vector3 toPlayer, float distToPlayer)
    {
        Vector3 faceDir = toPlayer;
        float targetSpeed = 0f;

        if (final == Final.Charge)
        {
            // A short pulse, then she lunges at her fastest... and stops right in front of you
            if (windup > 0f) windup -= dt;
            else targetSpeed = lungeSpeed * (1f + angerSpeedup) * 1.3f;

            if (distToPlayer <= 1.6f)
            {
                final = Final.Pause;
                finalTimer = FinalFaceTime;
                currentSpeed = 0f;
            }
        }
        else if (final == Final.Pause)
        {
            finalTimer -= dt;
            if (smoke != null) smoke.AddDarkness(-0.1f * dt);
            if (finalTimer <= 0f) final = Final.WalkToTree;
        }
        else if (final == Final.WalkToTree)
        {
            Vector3 toTree = Flat(BloomTree.Instance.transform.position - transform.position);
            faceDir = toTree;
            targetSpeed = walkSpeed * 0.8f;
            if (smoke != null) smoke.AddDarkness(-0.15f * dt);
            if (toTree.magnitude < 2f)
            {
                final = Final.WaitAtTree;
                finalTimer = FinalTogetherTime;
                currentSpeed = 0f;
            }
        }
        else if (final == Final.WaitAtTree)
        {
            // She waits by the tree for you to come and heal beside her
            if (smoke != null) smoke.AddDarkness(-0.05f * dt);
            if (BloomTree.Instance.IsNear(player.position)) finalTimer -= dt;
            else finalTimer = FinalTogetherTime;

            if (finalTimer <= 0f)
            {
                final = Final.Merging;
                finalTimer = MergeTime;
                mergeFrom = transform.position;
                mergeScale = transform.localScale;
                mergeBloomed = false;
                if (EndingManager.Instance != null) EndingManager.Instance.FinalReached();
            }
        }
        else if (final == Final.Merging)
        {
            // She drifts into the trunk, rising and shrinking, pale as light, while the ivy climbs
            finalTimer -= dt;
            float t = Mathf.Clamp01(1f - finalTimer / MergeTime);
            float ease = t * t * (3f - 2f * t);

            Vector3 trunk = BloomTree.Instance.transform.position + Vector3.up * 1.2f;
            transform.position = Vector3.Lerp(mergeFrom, trunk, ease);
            transform.localScale = Vector3.Lerp(mergeScale, mergeScale * 0.2f, ease);
            if (smoke != null) smoke.AddDarkness(-0.5f * dt);
            FaceTowards(BloomTree.Instance.transform.position);

            if (!mergeBloomed && t > 0.35f)
            {
                mergeBloomed = true;
                BloomTree.Instance.Blossom();
            }
            if (t >= 1f)
            {
                final = Final.Done;
                vanished = true;
                if (smoke != null) smoke.Vanish();
            }
            return;
        }

        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, (final == Final.Charge ? 10f : 4f) * dt);
        if (faceDir.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = faceDir.normalized;
            transform.position += dir * currentSpeed * dt;
            float leanTarget = windup > 0f ? -12f : final == Final.Charge ? 18f : 0f;
            currentLean = Mathf.Lerp(currentLean, leanTarget, 3f * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir) * Quaternion.Euler(currentLean, 0f, 0f), TurnSpeed * dt);
        }
        transform.position = OnGround(transform.position);
        if (hasSpeedParam) anim.SetFloat("Speed", currentSpeed);
    }

    // ---------- Helpers ----------

    Transform FindPlayer()
    {
        if (PlayerMovement.Instance != null) return PlayerMovement.Instance.transform;
        GameObject p = GameObject.Find("PLAYER");
        if (p == null) p = GameObject.FindWithTag("Player");
        if (p != null) return p.transform;
        if (PlayerHealth.Instance != null) return PlayerHealth.Instance.transform;
        if (Camera.main != null) return Camera.main.transform.root;
        return null;
    }

    // Ground height under a point (terrain, mountains, rocks)
    Vector3 OnGround(Vector3 p)
    {
        Vector3 from = new Vector3(p.x, p.y + 200f, p.z);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 500f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            Transform t = h.collider.transform;
            if (t == transform || t.IsChildOf(transform)) continue;
            if (player != null && (t == player || t.IsChildOf(player))) continue;
            if (h.point.y > best) best = h.point.y;
        }

        if (float.IsNegativeInfinity(best))
        {
            foreach (Terrain t in Terrain.activeTerrains)
            {
                Vector3 local = p - t.transform.position;
                Vector3 size = t.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                best = t.SampleHeight(p) + t.transform.position.y;
                break;
            }
        }

        if (!float.IsNegativeInfinity(best)) p.y = best;
        return p;
    }
}