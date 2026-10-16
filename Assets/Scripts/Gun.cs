using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Gun : MonoBehaviour
{
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float bulletSpeed = 60f;
    public float fireRate = 0.15f;
    public bool automatic = false;

    [Header("Recoil")]
    public Transform kickTarget; // empty = gun only, chest bone = arms too
    public float kickBack = 0.06f;
    public float kickUp = 6f;
    public float cameraKick = 1.5f;

    MouseLook mouseLook;
    float nextFireTime;
    Vector3 startPos, backDir, tiltAxis;
    Quaternion startRot;
    float target, current;

    void Start()
    {
        if (kickTarget == null) kickTarget = transform;
        startPos = kickTarget.localPosition;
        startRot = kickTarget.localRotation;

        Vector3 dir = firePoint.forward;
        Transform space = kickTarget.parent;
        backDir = space ? space.InverseTransformDirection(-dir) : -dir;
        tiltAxis = space ? space.InverseTransformDirection(Vector3.Cross(Vector3.up, dir).normalized) : Vector3.Cross(Vector3.up, dir).normalized;

        if (Camera.main) mouseLook = Camera.main.GetComponent<MouseLook>();
    }

    void Update()
    {
        if (WantsToShoot() && Time.time >= nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + fireRate;
        }
    }

    void LateUpdate()
    {
        target = Mathf.Lerp(target, 0f, 10f * Time.deltaTime);
        current = Mathf.Lerp(current, target, 30f * Time.deltaTime);

        kickTarget.localPosition = startPos + backDir * kickBack * current;
        kickTarget.localRotation = Quaternion.AngleAxis(-kickUp * current, tiltAxis) * startRot;
    }

    bool WantsToShoot()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        return automatic ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame;
#else
        return automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
#endif
    }

    void Shoot()
    {
        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        bullet.GetComponent<Rigidbody>().AddForce(firePoint.forward * bulletSpeed, ForceMode.VelocityChange);

        target += 1f;
        if (mouseLook) mouseLook.AddRecoil(cameraKick);
    }
}