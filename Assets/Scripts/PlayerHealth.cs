using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 3;
    public int currentHealth;

    public Slider healthBar;
    public UIManager uiManager;

    void Start()
    {
        currentHealth = maxHealth;

        healthBar.maxValue = maxHealth;
        healthBar.value = currentHealth;
    }

    void Update()
    {
        // Temporary test
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            TakeDamage();
        }
    }

    public void TakeDamage()
    {
        currentHealth--;

        healthBar.value = currentHealth;

        Debug.Log("Health: " + currentHealth);

        if (currentHealth <= 0)
        {
            Debug.Log("========== GAME OVER REACHED ==========");

            Debug.Log("UIManager reference: " + uiManager);

            uiManager.ShowGameOverMessage();

            Debug.Log("Game Over message function called");

            Time.timeScale = 0f;

            Debug.Log("TIME SCALE: " + Time.timeScale);
        }
    }
}