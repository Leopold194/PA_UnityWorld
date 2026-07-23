using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    public int team0Score = 0;
    public int team1Score = 0;

    public ScoreHUD hud;

    private void Awake()
    {
        Instance = this;
    }

    public void SetScore(int team0, int team1)
    {
        if (team0 == team0Score && team1 == team1Score) return;

        team0Score = team0;
        team1Score = team1;
        hud.SetScore(team0Score, team1Score);

        Debug.Log($"Score : {team0Score} - {team1Score}");
    }
}