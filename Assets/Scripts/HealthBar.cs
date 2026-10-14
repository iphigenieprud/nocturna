using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    public Slider slider;
    public PlayerHealth playerHealth;

    void Start()
    {
        slider.maxValue = playerHealth.maxHealth;
        slider.value = playerHealth.currentHealth;
    }

    void Update()
    {
        slider.value = playerHealth.currentHealth;
    }
}
