using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerShooting : MonoBehaviour
{
    public Camera playerCamera;
    public float range = 50f;

    private bool gameEnded = false;

    void Update()
    {
        if (gameEnded)
            return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log("LEFT CLICK DETECTED");
            Shoot();
        }
    }

    void Shoot()
    {
        Debug.Log("SHOOT FUNCTION STARTED");

        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, range))
        {
            Debug.Log("SHOT HIT: " + hit.collider.gameObject.name);

            EnemyHealth enemy = hit.collider.GetComponentInParent<EnemyHealth>();

            if (enemy != null)
            {
                Debug.Log("ENEMY HEALTH FOUND");
                enemy.TakeDamage();
            }
            else
            {
                Debug.Log("NO ENEMY HEALTH FOUND");
            }
        }
        else
        {
            Debug.Log("SHOT MISSED");
        }
    }

    public void EndGame()
    {
        gameEnded = true;
    }
}