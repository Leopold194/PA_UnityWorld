using UnityEngine;

public class GoalTrigger : MonoBehaviour
{
    public int teamScored;

    private bool scoredRecently = false;
    public float cooldown = 0.5f;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball"))
            return;

        if (scoredRecently)
            return;

        scoredRecently = true;
        Debug.Log($"Score for team {teamScored}!");
        ScoreManager.Instance.AddGoal(teamScored);

        Invoke(nameof(ResetCooldown), cooldown);
    }

    private void ResetCooldown()
    {
        scoredRecently = false;
    }
}