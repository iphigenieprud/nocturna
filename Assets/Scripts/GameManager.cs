using UnityEngine;

public class GameManager : MonoBehaviour
{
    public int score = 0;
    public int totalCollectibles = 4;
    public UIManager uiManager;

    public void AddScore()
    {
        score++;

        Debug.Log("Score: " + score);

        uiManager.UpdateScore(score);

        if (score >= totalCollectibles)
        {
            WinGame();
        }
    }

    void WinGame()
    {
        Debug.Log("YOU WIN!");

        uiManager.ShowWinMessage();

        Time.timeScale = 0f;
    }
}