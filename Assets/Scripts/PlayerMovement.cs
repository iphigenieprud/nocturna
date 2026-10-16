using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Put this on your PLAYER. Needs a Character Controller component.
// WASD to walk, Space to jump.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    private CharacterController controller;
    private float fallSpeed = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // Read WASD
        Vector2 input = GetMoveInput();

        // Move in the direction the player is facing
        Vector3 move = transform.right * input.x + transform.forward * input.y;
        controller.Move(move * speed * Time.deltaTime);

        // Gravity and jumping
        if (controller.isGrounded && fallSpeed < 0f)
        {
            fallSpeed = -2f; // keeps us stuck to the ground
        }

        if (controller.isGrounded && JumpPressed())
        {
            fallSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        fallSpeed += gravity * Time.deltaTime;
        controller.Move(Vector3.up * fallSpeed * Time.deltaTime);
    }

    Vector2 GetMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
        float y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
#else
        return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#endif
    }

    bool JumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetButtonDown("Jump");
#endif
    }
}