using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonCamera : MonoBehaviour
{
    public float mouseSensitivity = 0.5f;

    private float verticalRotation = 0f;

    void Update()
    {
        Vector2 mouse = Mouse.current.delta.ReadValue();

        float mouseX = mouse.x * mouseSensitivity;
        float mouseY = mouse.y * mouseSensitivity;

        // Rotate the player left and right
        transform.parent.Rotate(0f, mouseX, 0f);

        // Rotate the camera up and down
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -80f, 80f);

        transform.localRotation = Quaternion.Euler(
            verticalRotation,
            0f,
            0f
        );
    }
}