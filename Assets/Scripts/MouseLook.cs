using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)]
public class MouseLook : MonoBehaviour
{
    public Transform player;
    public Renderer[] hideInFirstPerson;
    public Vector3 thirdPersonOffset = new Vector3(0.6f, 1.7f, -2.5f);
    [Range(0, 1)] public float stabilize = 0.9f;
    public float smoothSpeed = 20f;
    public float fieldOfView = 65f;
    public float mouseSensitivity = 3f;
    public float minPitch = -70f;
    public float maxPitch = 50f;
    public bool thirdPerson = false;

    Transform head;
    Camera cam;
    Vector3 startPos;
    Quaternion startRot;
    Quaternion smoothRot;
    float pitch;
    float recoil;

    void Start()
    {
        Animator anim = player.GetComponentInChildren<Animator>();
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        cam = GetComponent<Camera>();
        cam.nearClipPlane = 0.01f;
        startPos = transform.localPosition;
        startRot = transform.localRotation;
        smoothRot = transform.rotation;
    }

    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.V)) thirdPerson = !thirdPerson;

        cam.fieldOfView = fieldOfView;

        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        recoil = Mathf.Lerp(recoil, 0, 10f * Time.deltaTime);

        if (thirdPerson)
        {
            Quaternion rot = Quaternion.Euler(pitch - recoil, player.eulerAngles.y, 0);
            transform.position = player.position + rot * thirdPersonOffset;
            transform.rotation = rot;
            smoothRot = rot;
            head.localScale = Vector3.one;
            SetHidden(false);
        }
        else
        {
            transform.localPosition = startPos;

            Quaternion neckRot = transform.parent.rotation * startRot * Quaternion.Euler(pitch - recoil, 0, 0);
            Quaternion level = Quaternion.LookRotation(neckRot * Vector3.forward, Vector3.up);
            Quaternion steady = Quaternion.Euler(pitch - recoil, player.eulerAngles.y, 0);

            Quaternion target = Quaternion.Slerp(level, steady, stabilize);
            smoothRot = Quaternion.Slerp(smoothRot, target, smoothSpeed * Time.deltaTime);
            transform.rotation = smoothRot;

            head.localScale = Vector3.zero;
            SetHidden(true);
        }
    }

    void SetHidden(bool hidden)
    {
        foreach (Renderer r in hideInFirstPerson)
        {
            if (r == null) continue;
            r.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }

    public void AddRecoil(float vertical) { recoil += vertical; }
    public void AddRecoil(float vertical, float horizontal) { recoil += vertical; }
    public void AddRecoil(Vector2 r) { recoil += r.y; }
}