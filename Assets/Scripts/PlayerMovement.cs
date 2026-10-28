using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public static PlayerMovement Instance;

    public Animator anim;
    public float walkSpeed = 4f;
    [Tooltip("Speed while holding Shift (sprinting). It uses stamina.")]
    public float runSpeed = 5f;
    public float mouseSensitivity = 3f;

    public int magazineSize = 8;
    public float reloadTime = 1.5f;
    public int ammo;

    public GameObject handMagazine;
    public float magShowTime = 2f;

    [Header("Stamina (sprint and jump)")]
    public float maxStamina = 100f;
    [Tooltip("Stamina used per second while sprinting.")]
    public float staminaDrain = 35f;
    [Tooltip("Stamina back per second when not sprinting.")]
    public float staminaRegen = 25f;

    [Header("Jump")]
    [Tooltip("How high you jump (meters).")]
    public float jumpHeight = 1.1f;
    public float gravity = 20f;
    [Tooltip("Stamina each jump uses. 0 = free.")]
    public float jumpStaminaCost = 10f;

    const float StartCost = 15f;    // stamina needed to sprint again after running out
    const float SafeTime = 0.35f;   // right after you start sprinting, she can't hurt you
    const float RegenDelay = 0.8f;  // pause after using stamina before it comes back
    const float StepHeight = 0.5f;  // you walk up bumps and slopes this high without jumping

    public float Stamina { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool Grounded { get; private set; } = true;

    // Used by PlayerHealth: true right after a sprint starts (lets you slip past her lunge)
    public bool IsSafe { get { return IsSprinting && sprintTime < SafeTime; } }

    Transform hips;
    Vector3 hipsStart;
    bool reloading;
    float reloadTimer;
    float magTimer;

    bool exhausted;
    float sprintTime;
    float sinceStamina = 99f;
    float verticalSpeed;

    void Awake() { Instance = this; }

    void Start()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        hipsStart = transform.InverseTransformPoint(hips.position);
        Cursor.lockState = CursorLockMode.Locked;
        ammo = magazineSize;
        if (handMagazine != null) handMagazine.SetActive(false);
        Stamina = maxStamina;
        UpdateStaminaUI();
    }

    void Update()
    {
        transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);

        float x = 0;
        float y = 0;

        if (Input.GetKey(KeyCode.W)) y = 1;
        if (Input.GetKey(KeyCode.S)) y = -1;
        if (Input.GetKey(KeyCode.A)) x = -1;
        if (Input.GetKey(KeyCode.D)) x = 1;

        bool moving = x != 0 || y != 0;
        bool running = UpdateSprint(moving);
        bool runBack = running && y < 0 && x == 0;
        float speed = running ? runSpeed : walkSpeed;

        UpdateJumpAndGround();

        if (magTimer > 0)
        {
            magTimer -= Time.deltaTime;
            if (magTimer <= 0 && handMagazine != null) handMagazine.SetActive(false);
        }

        if (moving)
        {
            transform.Translate(new Vector3(x, 0, y).normalized * speed * Time.deltaTime);
            anim.SetFloat("MoveX", x);
            anim.SetFloat("MoveY", y);
            anim.SetInteger("Mode", (running && !runBack) ? 1 : 0);
            anim.SetFloat("AnimSpeed", runBack ? 1.6f : 1);
            anim.ResetTrigger("Shoot");
            anim.ResetTrigger("Reload");
            if (reloading)
            {
                reloading = false;
                magTimer = 0;
                if (handMagazine != null) handMagazine.SetActive(false);
            }
        }
        else
        {
            anim.SetFloat("AnimSpeed", 0);

            if (reloading)
            {
                reloadTimer -= Time.deltaTime;
                if (reloadTimer <= 0)
                {
                    ammo = magazineSize;
                    reloading = false;
                }
            }
            else if (Input.GetKeyDown(KeyCode.R) && ammo < magazineSize)
            {
                StartReload();
            }
            else if (Input.GetMouseButtonDown(0))
            {
                if (ammo > 0)
                {
                    anim.SetTrigger("Shoot");
                    ammo--;
                }
                else
                {
                    StartReload();
                }
            }
        }
    }

    void StartReload()
    {
        reloading = true;
        reloadTimer = reloadTime;
        anim.SetTrigger("Reload");

        magTimer = magShowTime;
        if (handMagazine != null) handMagazine.SetActive(true);
    }

    void LateUpdate()
    {
        Vector3 p = transform.InverseTransformPoint(hips.position);
        p.x = hipsStart.x;
        p.z = hipsStart.z;
        hips.position = transform.TransformPoint(p);
    }

    // ---------- Sprint (hold Shift, uses stamina) ----------

    // Returns true while you're sprinting
    bool UpdateSprint(bool moving)
    {
        float dt = Time.deltaTime;
        bool shift = Input.GetKey(KeyCode.LeftShift);

        // After running out, you need a little stamina back before sprinting again
        if (exhausted && Stamina >= StartCost) exhausted = false;

        if (!IsSprinting && shift && moving)
        {
            if (!exhausted)
            {
                IsSprinting = true;
                sprintTime = 0f;
            }
            else if (Input.GetKeyDown(KeyCode.LeftShift) && UIManager.Instance != null) UIManager.Instance.NoStamina();
        }

        if (IsSprinting && (!shift || !moving || Stamina <= 0f))
        {
            if (Stamina <= 0f)
            {
                exhausted = true;
                if (UIManager.Instance != null) UIManager.Instance.NoStamina();
            }
            IsSprinting = false;
        }

        if (IsSprinting)
        {
            sprintTime += dt;
            sinceStamina = 0f;
            Stamina = Mathf.Max(0f, Stamina - staminaDrain * dt);
            UpdateStaminaUI();
        }
        else
        {
            sinceStamina += dt;
            if (sinceStamina > RegenDelay && Stamina < maxStamina)
            {
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * dt);
                UpdateStaminaUI();
            }
        }

        return IsSprinting;
    }

    // Other scripts can use stamina through this. Returns false if there isn't enough.
    public bool TrySpend(float amount)
    {
        if (Stamina < amount)
        {
            if (UIManager.Instance != null) UIManager.Instance.NoStamina();
            return false;
        }
        Stamina -= amount;
        sinceStamina = 0f;
        UpdateStaminaUI();
        return true;
    }

    void UpdateStaminaUI()
    {
        if (UIManager.Instance != null) UIManager.Instance.SetStamina(Stamina, maxStamina);
    }

    // ---------- Jump and staying on the ground ----------

    void UpdateJumpAndGround()
    {
        float dt = Time.deltaTime;
        Vector3 pos = transform.position;
        float ground;
        bool hasGround = GroundBelow(pos, out ground);

        Grounded = hasGround && verticalSpeed <= 0f && pos.y - ground < 0.05f;

        // Jump (Space)
        if (Input.GetKeyDown(KeyCode.Space) && Grounded && (jumpStaminaCost <= 0f || TrySpend(jumpStaminaCost)))
        {
            verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
            Grounded = false;
        }

        if (Grounded)
        {
            // Follow the ground: up slopes and bumps, down gentle drops
            verticalSpeed = 0f;
            pos.y = ground;
        }
        else
        {
            // In the air: rise, slow down, fall
            verticalSpeed -= gravity * dt;
            pos.y += verticalSpeed * dt;

            // Hit something above your head: start falling
            if (verticalSpeed > 0f && Physics.Raycast(transform.position + Vector3.up * 1.6f, Vector3.up, verticalSpeed * dt + 0.1f, ~0, QueryTriggerInteraction.Ignore))
                verticalSpeed = 0f;

            // Landed
            if (hasGround && pos.y <= ground)
            {
                pos.y = ground;
                verticalSpeed = 0f;
            }
        }

        transform.position = pos;
    }

    // Height of the ground under you (ignores your own body). Only counts ground you could step onto.
    bool GroundBelow(Vector3 p, out float groundY)
    {
        groundY = p.y;
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * StepHeight, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform.root)) continue;
            if (hit.point.y > best) best = hit.point.y;
        }
        if (float.IsNegativeInfinity(best)) return false;
        groundY = best;
        return true;
    }

    // Backup stamina bar when there's no UIManager
    void OnGUI()
    {
        if (UIManager.Instance != null || Event.current.type != EventType.Repaint) return;
        if (Stamina >= maxStamina) return;

        float k = Stamina / Mathf.Max(1f, maxStamina);
        GUI.color = new Color(0f, 0f, 0f, 0.4f);
        GUI.DrawTexture(new Rect(60, Screen.height - 50, 200, 4), Texture2D.whiteTexture);
        GUI.color = new Color(0.8f, 0.85f, 0.9f, 0.7f);
        GUI.DrawTexture(new Rect(60, Screen.height - 50, 200 * k, 4), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}