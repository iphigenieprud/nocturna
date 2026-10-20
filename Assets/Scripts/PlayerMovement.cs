using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public Animator anim;
    public float walkSpeed = 2f;
    public float runSpeed = 5f;
    public float mouseSensitivity = 3f;

    public int magazineSize = 8;
    public float reloadTime = 1.5f;
    public int ammo;

    public GameObject handMagazine;
    public float magShowTime = 2f;

    Transform hips;
    Vector3 hipsStart;
    bool reloading;
    float reloadTimer;
    float magTimer;

    void Start()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        hipsStart = transform.InverseTransformPoint(hips.position);
        Cursor.lockState = CursorLockMode.Locked;
        ammo = magazineSize;
        if (handMagazine != null) handMagazine.SetActive(false);
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

        bool running = Input.GetKey(KeyCode.LeftShift);
        bool runBack = running && y < 0 && x == 0;
        float speed = running ? runSpeed : walkSpeed;

        if (magTimer > 0)
        {
            magTimer -= Time.deltaTime;
            if (magTimer <= 0 && handMagazine != null) handMagazine.SetActive(false);
        }

        if (x != 0 || y != 0)
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
}