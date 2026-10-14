using UnityEngine;

public class Collectible : MonoBehaviour
{
    public GameManager gameManager;

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            gameManager.AddScore();
            Destroy(gameObject);
        }
    }
}