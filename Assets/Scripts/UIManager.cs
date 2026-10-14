using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI winMessage;
    public TextMeshProUGUI lostMessage;

    public void UpdateScore(int score)
    {
        scoreText.text = "Score: " + score;
    }

    public void ShowWinMessage()
    {
        winMessage.gameObject.SetActive(true);
    }

    public void ShowGameOverMessage()
    {
        Debug.Log("SHOW GAME OVER CALLED");

        if (lostMessage == null)
        {
            Debug.LogError("lostMessage is NULL!");
            return;
        }

        Debug.Log("lostMessage = " + lostMessage.gameObject.name);

        lostMessage.gameObject.SetActive(true);
    }
}