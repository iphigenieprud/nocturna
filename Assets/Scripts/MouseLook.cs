using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MouseLook : MonoBehaviour
{
    public Transform playerBody;
    public float sensitivity = 1f;
    public float maxLookUp = 80f;
    public float maxLookDown = 80f;

    float xRotation;
    float targetRecoil, currentRecoil;

    public void AddRecoil(float amount)
    {
        targetRecoil += amount;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        Vector2 mouse = GetMouseDelta() * sensitivity;

        xRotation = Mathf.Clamp(xRotation - mouse.y, -maxLookUp, maxLookDown);
        playerBody.Rotate(Vector3.up * mouse.x);

        targetRecoil = Mathf.Lerp(targetRecoil, 0f, 6f * Time.deltaTime);
        currentRecoil = Mathf.Lerp(currentRecoil, targetRecoil, 25f * Time.deltaTime);

        transform.localRotation = Quaternion.Euler(xRotation - currentRecoil, 0f, 0f);
    }

    Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return Vector2.zero;
        return Mouse.current.delta.ReadValue() * 0.1f;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }
}