using UnityEngine;
using System.Collections.Generic;

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

    [Header("Mode jouer : joueurs humains")]
    [Tooltip("Indices (dans currentState.players[]) des joueurs contrôlés par un humain en mode jouer. " +
             "À renseigner selon votre mapping lobby/manette (ex: 0 pour le joueur local). " +
             "Peut aussi être défini au runtime via SetHumanPlayers().")]
    public int[] humanPlayerIndices = new int[0];

    [Header("Halo de tir (joueurs humains)")]
    [Tooltip("Couleur du halo qui apparaît autour d'un joueur humain quand il tire.")]
    public Color haloColor = new Color(1f, 0.85f, 0.15f, 0.9f);
    [Tooltip("Durée de l'animation du halo (expansion + fondu).")]
    public float haloDuration = 0.35f;
    [Tooltip("Épaisseur du trait du halo.")]
    public float haloLineWidth = 0.08f;
    [Tooltip("Hauteur au sol à laquelle le halo est dessiné.")]
    public float haloHeight = 0.03f;
    [Tooltip("Nombre de segments du cercle du halo.")]
    public int haloSegments = 40;
    [Tooltip("Marge par rapport au rayon du joueur pour le rayon de départ du halo.")]
    public float haloStartRadiusMultiplier = 1.1f;

    [Header("Flèche façon FIFA (joueurs humains)")]
    [Tooltip("Couleur de la petite flèche affichée au-dessus des joueurs humains.")]
    public Color arrowColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Hauteur de la flèche au-dessus du joueur.")]
    public float arrowHeightAbovePlayer = 2.2f;
    [Tooltip("Amplitude du léger mouvement de flottement de la flèche.")]
    public float arrowBobAmplitude = 0.15f;
    [Tooltip("Vitesse du flottement de la flèche.")]
    public float arrowBobSpeed = 4f;
    [Tooltip("Taille (demi-largeur/hauteur) de la flèche.")]
    public float arrowSize = 0.35f;

    // Valeurs reprises de continuous_football_env.rs (Rust) pour que les effets
    // visuels restent cohérents avec les valeurs réelles de la simulation :
    // PLAYER_RADIUS = 55.0, KICK_RADIUS = 120.0 (unités du terrain Rust, à multiplier par worldScale).
    const float RustPlayerRadius = 55f;
    const float RustKickRadius = 120f;

    WorldState currentState;
    WorldState previousState;
    float lastReceiveTime;

    Transform ballTransform;
    Transform[] playerTransforms;
    Animator[] playerAnimators;
    float[] kickPulseTimers;
    float[] dashStretchTimers;
    Vector3[] baseScales;

    HashSet<int> humanPlayerSet;
    LineRenderer[] haloRenderers;
    float[] haloTimers;
    Transform[] arrowTransforms;

    bool spawned = false;

    void OnEnable()
    {
        Debug.Log("[GameStateReceiver] OnEnable / abonnement");
        humanPlayerSet = new HashSet<int>(humanPlayerIndices);
        GameNetworkClient.Instance.OnWorldState += HandleWorldState;
    }

    public void SetHumanPlayers(IEnumerable<int> indices)
    {
        humanPlayerSet = new HashSet<int>(indices);
        if (playerTransforms != null) RebuildHumanVisuals(playerTransforms.Length);
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

        RebuildHumanVisuals(n);
    }

    void RebuildHumanVisuals(int n)
    {
        if (haloRenderers != null)
            foreach (var lr in haloRenderers)
                if (lr != null) Destroy(lr.gameObject);
        if (arrowTransforms != null)
            foreach (var arr in arrowTransforms)
                if (arr != null) Destroy(arr.gameObject);

        haloRenderers = new LineRenderer[n];
        haloTimers = new float[n];
        arrowTransforms = new Transform[n];

        for (int i = 0; i < n; i++)
        {
            if (humanPlayerSet == null || !humanPlayerSet.Contains(i)) continue;
            haloRenderers[i] = CreateHalo(i);
            arrowTransforms[i] = CreateArrow(i);
        }
    }

    LineRenderer CreateHalo(int index)
    {
        var go = new GameObject($"HumanHalo_{index}");
        var lr = go.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.useWorldSpace = true;
        lr.widthMultiplier = haloLineWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = haloColor;
        lr.endColor = haloColor;
        lr.numCapVertices = 4;
        lr.positionCount = haloSegments + 1;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        go.SetActive(false);
        return lr;
    }

    Transform CreateArrow(int index)
    {
        var go = new GameObject($"HumanArrow_{index}");
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.mesh = BuildArrowMesh(arrowSize);
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = arrowColor;
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }

    Mesh BuildArrowMesh(float size)
    {
        // Petit triangle pointant vers le bas, comme le repère au-dessus du joueur contrôlé dans FIFA.
        var mesh = new Mesh { name = "FifaArrow" };
        Vector3[] verts =
        {
            new Vector3(-size, size, 0f),
            new Vector3(size, size, 0f),
            new Vector3(0f, -size, 0f),
        };
        // Deux triangles (recto/verso) pour rester visible quelle que soit l'orientation de la caméra.
        int[] tris = { 0, 1, 2, 0, 2, 1 };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void UpdateHaloPoints(LineRenderer lr, Vector3 center, float radius)
    {
        int count = haloSegments + 1;
        if (lr.positionCount != count) lr.positionCount = count;
        for (int s = 0; s < count; s++)
        {
            float angle = (s / (float)haloSegments) * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(angle) * radius, haloHeight, Mathf.Sin(angle) * radius);
            lr.SetPosition(s, p);
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

        bool wasKicking = prev != null && prev.kick_timer != 0;
        bool isKicking = cur.kick_timer != 0;

        if (anim != null)
        {
            float speed = new Vector2(cur.vx, cur.vy).magnitude;
            anim.SetFloat("Speed", speed);

            if (isKicking && !wasKicking) { anim.SetTrigger("Kick"); kickPulseTimers[index] = kickPulseDuration; }

            bool wasDashing = prev != null && prev.dash_timer != 0;
            bool isDashing = cur.dash_timer != 0;
            if (isDashing && !wasDashing) dashStretchTimers[index] = dashStretchDuration;
        }

        // Halo de tir : uniquement pour les joueurs humains, déclenché au début du tir (front montant du kick).
        if (isKicking && !wasKicking && humanPlayerSet != null && humanPlayerSet.Contains(index))
            haloTimers[index] = haloDuration;

        UpdateHumanHalo(index, tr.position);
        UpdateHumanArrow(index, tr.position);

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

    void UpdateHumanHalo(int index, Vector3 playerWorldPos)
    {
        LineRenderer lr = haloRenderers[index];
        if (lr == null) return;

        if (haloTimers[index] > 0f)
        {
            haloTimers[index] -= Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(haloTimers[index] / haloDuration); // 0 (début) -> 1 (fin)

            float startRadius = RustPlayerRadius * worldScale * haloStartRadiusMultiplier;
            float endRadius = RustKickRadius * worldScale;
            float radius = Mathf.Lerp(startRadius, endRadius, progress);

            Color c = haloColor;
            c.a = haloColor.a * (1f - progress);
            lr.startColor = c;
            lr.endColor = c;

            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            UpdateHaloPoints(lr, playerWorldPos, radius);
        }
        else if (lr.gameObject.activeSelf)
        {
            lr.gameObject.SetActive(false);
        }
    }

    void UpdateHumanArrow(int index, Vector3 playerWorldPos)
    {
        Transform arrowTr = arrowTransforms[index];
        if (arrowTr == null) return;

        float bob = Mathf.Sin(Time.time * arrowBobSpeed) * arrowBobAmplitude;
        arrowTr.position = playerWorldPos + Vector3.up * (arrowHeightAbovePlayer + bob);

        Camera cam = Camera.main;
        if (cam != null)
            arrowTr.rotation = Quaternion.LookRotation(arrowTr.position - cam.transform.position, Vector3.up);
    }
}