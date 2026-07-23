using TMPro;
using UnityEngine;

public class ScoreHUD : MonoBehaviour
{
    public TMP_Text team0Text;
    public TMP_Text team1Text;

    public void SetScore(int t0, int t1)
    {
        team0Text.text = t0.ToString();
        team1Text.text = t1.ToString();
    }
}