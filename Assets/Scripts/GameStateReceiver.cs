using UnityEngine;

[System.Serializable]
public class WorldState { public ulong tick; public EntityState ball; public PlayerState[] players; }
[System.Serializable]
public class EntityState { public float x, y, vx, vy; }
[System.Serializable]
public class PlayerState { public float x, y, vx, vy, look_x, look_y; public byte dash_timer, kick_timer, team; }

public class GameStateReceiver : MonoBehaviour
{
    [Header("Prefabs à assigner dans l'inspecteur")]
    public Transform ballPrefab;
    public Transform slimePrefab;
    public Transform turtleShellPrefab;

    [Header("Debug visuel du kick")]
    public float kickScalePulse = 1.8f;
    public float kickPulseDuration = 0.3f;

    [Header("Debug visuel du dash")]
    public float dashStretch = 1.4f;
    public float dashStretchDuration = 0.25f;

    [Header("Mapping terrain")]
    public float fieldWidth;
    public float fieldHeight;
    public float worldScale;

    [Header("Rotation du ballon")]
    public float ballRadius = 0.3f;
    public bool autoDetectRadius = true;

    [Header("Doit matcher tick_duration côté Rust")]
    public float receiveInterval = 1f / 60f;

    WorldState currentState;
    WorldState previousState;
    float lastReceiveTime;

    Transform ballTransform;
    Transform[] playerTransforms;
    Animator[] playerAnimators;
    float[] kickPulseTimers;
    float[] dashStretchTimers;
    Vector3[] baseScales;

    bool spawned = false;

    void OnEnable()
    {
        Debug.Log("[GameStateReceiver] OnEnable / abonnement");
        GameNetworkClient.Instance.OnWorldState += HandleWorldState;
    }
    void OnDisable() { if (GameNetworkClient.Instance != null) GameNetworkClient.Instance.OnWorldState -= HandleWorldState; }

    void EnsureBallSpawned()
    {
        if (spawned) return;
        ballTransform = Instantiate(ballPrefab);
        ballTransform.name = "Ball";

        if (autoDetectRadius)
        {
            var renderer = ballTransform.GetComponentInChildren<Renderer>();
            if (renderer != null)
                ballRadius = (renderer.bounds.extents.x + renderer.bounds.extents.z) * 0.5f;
        }
        spawned = true;
    }

    void EnsurePlayersSpawned(PlayerState[] players)
    {
        int n = players.Length;
        if (playerTransforms != null && playerTransforms.Length == n) return;

        if (playerTransforms != null)
            foreach (var t in playerTransforms)
                if (t != null) Destroy(t.gameObject);

        playerTransforms = new Transform[n];
        playerAnimators = new Animator[n];
        kickPulseTimers = new float[n];
        dashStretchTimers = new float[n];
        baseScales = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            Transform prefab = players[i].team == 0 ? slimePrefab : turtleShellPrefab;
            playerTransforms[i] = Instantiate(prefab);
            playerTransforms[i].name = $"Player_{i}_{(players[i].team == 0 ? "Slime" : "TurtleShell")}";
            playerAnimators[i] = playerTransforms[i].GetComponentInChildren<Animator>();
            baseScales[i] = playerTransforms[i].localScale;
        }
    }

    Vector3 ToWorld(float x, float y)
    {
        float wx = (x - fieldWidth * 0.5f) * worldScale;
        float wz = (y - fieldHeight * 0.5f) * worldScale;
        return new Vector3(wx, 0f, wz);
    }

    void HandleWorldState(WorldState state)
    {
        Debug.Log($"[GameStateReceiver] frame reçue, tick={state.tick}, players={state.players?.Length}");

        EnsureBallSpawned();
        previousState = currentState;
        currentState = state;
        lastReceiveTime = Time.realtimeSinceStartup;
    }

    void Update()
    {
        if (currentState == null) return;

        EnsurePlayersSpawned(currentState.players);

        float t = previousState == null
            ? 1f
            : Mathf.Clamp01((Time.realtimeSinceStartup - lastReceiveTime) / receiveInterval);

        ApplyBall(currentState.ball, previousState?.ball, t);

        for (int i = 0; i < currentState.players.Length; i++)
        {
            PlayerState prev = (previousState != null && i < previousState.players.Length)
                ? previousState.players[i] : null;
            ApplyPlayer(playerTransforms[i], playerAnimators[i], currentState.players[i], prev, t, i);
        }
    }

    void ApplyBall(EntityState cur, EntityState prev, float t)
    {
        Vector3 target = ToWorld(cur.x, cur.y);
        Vector3 previousPos = ballTransform.position;
        ballTransform.position = prev != null ? Vector3.Lerp(ToWorld(prev.x, prev.y), target, t) : target;

        Vector3 delta = ballTransform.position - previousPos;
        float distance = delta.magnitude;
        if (distance > 0.0001f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, delta.normalized);
            float angleDegrees = (distance / ballRadius) * Mathf.Rad2Deg;
            ballTransform.Rotate(axis, angleDegrees, Space.World);
        }
    }

    void ApplyPlayer(Transform tr, Animator anim, PlayerState cur, PlayerState prev, float t, int index)
    {
        Vector3 target = ToWorld(cur.x, cur.y);
        tr.position = prev != null ? Vector3.Lerp(ToWorld(prev.x, prev.y), target, t) : target;

        Vector3 lookDir = new Vector3(cur.look_x, 0f, cur.look_y);
        if (lookDir.sqrMagnitude > 1e-4f)
            tr.rotation = Quaternion.LookRotation(lookDir);

        if (anim != null)
        {
            float speed = new Vector2(cur.vx, cur.vy).magnitude;
            anim.SetFloat("Speed", speed);

            bool wasKicking = prev != null && prev.kick_timer != 0;
            bool isKicking = cur.kick_timer != 0;
            if (isKicking && !wasKicking) { anim.SetTrigger("Kick"); kickPulseTimers[index] = kickPulseDuration; }

            bool wasDashing = prev != null && prev.dash_timer != 0;
            bool isDashing = cur.dash_timer != 0;
            if (isDashing && !wasDashing) dashStretchTimers[index] = dashStretchDuration;
        }

        if (kickPulseTimers[index] > 0f)
        {
            kickPulseTimers[index] -= Time.deltaTime;
            float pulse = Mathf.Sin(Mathf.Clamp01(kickPulseTimers[index] / kickPulseDuration) * Mathf.PI);
            tr.localScale = baseScales[index] * (1f + (kickScalePulse - 1f) * pulse);
        }
        else if (dashStretchTimers[index] > 0f)
        {
            dashStretchTimers[index] -= Time.deltaTime;
            float pulse = Mathf.Sin(Mathf.Clamp01(dashStretchTimers[index] / dashStretchDuration) * Mathf.PI);
            float stretch = 1f + (dashStretch - 1f) * pulse;
            float squash = 1f / Mathf.Sqrt(stretch);
            tr.localScale = Vector3.Scale(baseScales[index], new Vector3(squash, squash, stretch));
        }
        else tr.localScale = baseScales[index];
    }
}