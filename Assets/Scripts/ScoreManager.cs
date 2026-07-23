using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    public int team0Score = 0;
    public int team1Score = 0;

    public ScoreHUD hud; // <-- référence vers ton HUD

    private void Awake()
    {
        Instance = this;
    }

    public void AddGoal(int team)
    {
        if (team == 0)
        {
            team0Score++;
        }
        else
        {
            team1Score++;
        }

        // Met à jour l'affichage du HUD
        hud.SetScore(team0Score, team1Score);

        Debug.Log($"Score : {team0Score} - {team1Score}");
    }
}