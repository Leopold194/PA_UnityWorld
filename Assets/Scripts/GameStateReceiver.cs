using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class WorldState {
    public ulong tick;
    public EntityState ball;
    public PlayerState[] players;
    public int team0_score;
    public int team1_score;
}
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
             "Renseigné automatiquement à la réception de 'game_start' depuis le serveur (lobby_server), " +
             "qui connaît le mapping réel joueur humain/slot. Valeur ci-dessous uniquement utilisée en " +
             "fallback (tests hors connexion) tant qu'aucun game_start n'a été reçu.")]
    public int[] humanPlayerIndices = new int[0];

    [Header("Anneau de tir (joueurs humains)")]
    [Tooltip("Couleur de l'anneau qui apparaît au sol autour d'un joueur humain quand il tire.")]
    public Color haloColor = new Color(1f, 0.85f, 0.15f, 0.9f);
    [Tooltip("Durée de l'animation de l'anneau (expansion + fondu).")]
    public float haloDuration = 0.35f;
    [Tooltip("Épaisseur de l'anneau, en fraction du rayon (0-0.5).")]
    public float haloLineWidth = 0.08f;
    [Tooltip("Hauteur au sol à laquelle l'anneau est dessiné (évite le z-fighting avec le sol).")]
    public float haloHeight = 0.03f;
    [Tooltip("Marge par rapport au rayon du joueur pour le rayon de départ de l'anneau.")]
    public float haloStartRadiusMultiplier = 1.1f;

    [Header("Flèche façon FIFA (joueurs humains)")]
    [Tooltip("Couleur de la petite flèche affichée au-dessus des joueurs humains.")]
    public Color arrowColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Marge ajoutée au-dessus du sommet détecté du joueur (bounds du renderer), pour que la " +
             "flèche reste juste au-dessus de la tête quelle que soit la taille réelle du prefab.")]
    public float arrowHeightAbovePlayer = 0.4f;
    [Tooltip("Amplitude du léger mouvement de flottement de la flèche.")]
    public float arrowBobAmplitude = 0.15f;
    [Tooltip("Vitesse du flottement de la flèche.")]
    public float arrowBobSpeed = 4f;
    [Tooltip("Taille de la flèche, exprimée en fraction de la largeur détectée du joueur (bounds du " +
             "renderer). Ex: 0.8 = flèche aussi large que 80% du joueur.")]
    public float arrowSize = 0.5f;

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
    float[] playerHeights;
    float[] playerWidths;

    HashSet<int> humanPlayerSet;
    Transform[] kickRingTransforms;
    Material[] kickRingMaterials;
    float[] haloTimers;
    Transform[] arrowTransforms;

    static Mesh groundQuadMesh;
    static Mesh billboardQuadMesh;

    bool spawned = false;

    void OnEnable()
    {
        Debug.Log("[GameStateReceiver] OnEnable / abonnement");
        humanPlayerSet = new HashSet<int>(humanPlayerIndices);
        GameNetworkClient.Instance.OnWorldState += HandleWorldState;
        GameNetworkClient.Instance.OnGameStart += HandleGameStart;
    }

    public void SetHumanPlayers(IEnumerable<int> indices)
    {
        humanPlayerSet = new HashSet<int>(indices);
        if (playerTransforms != null) RebuildHumanVisuals(playerTransforms.Length);
    }

    // Détruit toutes les entités instanciées pour la partie en cours (ballon, joueurs,
    // anneaux de tir, flèches) et remet l'état à zéro, pour repartir sur une base propre
    // au prochain game_start (ex: après un retour au menu).
    public void ResetGame()
    {
        currentState = null;
        previousState = null;

        if (ballTransform != null) Destroy(ballTransform.gameObject);
        ballTransform = null;
        spawned = false;

        if (playerTransforms != null)
            foreach (var t in playerTransforms)
                if (t != null) Destroy(t.gameObject);
        if (kickRingTransforms != null)
            foreach (var t in kickRingTransforms)
                if (t != null) Destroy(t.gameObject);
        if (arrowTransforms != null)
            foreach (var t in arrowTransforms)
                if (t != null) Destroy(t.gameObject);

        playerTransforms = null;
        playerAnimators = null;
        kickPulseTimers = null;
        dashStretchTimers = null;
        baseScales = null;
        playerHeights = null;
        playerWidths = null;
        kickRingTransforms = null;
        kickRingMaterials = null;
        haloTimers = null;
        arrowTransforms = null;
    }

    void HandleGameStart(GameStartMsg msg)
    {
        var indices = new List<int>();
        if (msg?.slots != null)
        {
            for (int i = 0; i < msg.slots.Length; i++)
                if (msg.slots[i] != -1) indices.Add(i);
        }
        Debug.Log($"[GameStateReceiver] game_start reçu, joueurs humains = [{string.Join(", ", indices)}]");
        SetHumanPlayers(indices);
    }

    void OnDisable()
    {
        if (GameNetworkClient.Instance == null) return;
        GameNetworkClient.Instance.OnWorldState -= HandleWorldState;
        GameNetworkClient.Instance.OnGameStart -= HandleGameStart;
    }

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
        playerHeights = new float[n];
        playerWidths = new float[n];

        for (int i = 0; i < n; i++)
        {
            Transform prefab = players[i].team == 0 ? slimePrefab : turtleShellPrefab;
            playerTransforms[i] = Instantiate(prefab);
            playerTransforms[i].name = $"Player_{i}_{(players[i].team == 0 ? "Slime" : "TurtleShell")}";
            playerAnimators[i] = playerTransforms[i].GetComponentInChildren<Animator>();
            baseScales[i] = playerTransforms[i].localScale;

            // Détection auto des dimensions réelles du prefab (comme autoDetectRadius pour le ballon),
            // pour placer/dimensionner la flèche relativement à la vraie taille du personnage plutôt
            // qu'avec des valeurs fixes qui ne correspondent à aucune échelle en particulier.
            //
            // On calcule à partir de sharedMesh.bounds (donnée statique de l'asset, toujours valide
            // immédiatement) plutôt que Renderer.bounds : pour un SkinnedMeshRenderer fraîchement
            // instancié, Renderer.bounds peut ne pas encore refléter la transformation monde réelle
            // (recalcul lié au rendu/skinning, pas garanti synchrone avec Instantiate()).
            //
            // La hauteur est mesurée comme (haut réel du mesh - position du pivot), pas extents.y*2 :
            // le pivot de ces prefabs est aux pieds, pas au centre vertical du mesh, donc extents*2
            // sous-estimait la hauteur réelle et plaçait la flèche à mi-corps.
            Bounds? worldBounds = ComputeRendererWorldBounds(playerTransforms[i]);
            if (worldBounds.HasValue)
            {
                playerHeights[i] = worldBounds.Value.max.y - playerTransforms[i].position.y;
                playerWidths[i] = Mathf.Max(worldBounds.Value.extents.x, worldBounds.Value.extents.z) * 2f;
            }
            else
            {
                playerHeights[i] = 2f;
                playerWidths[i] = 1f;
            }
        }

        RebuildHumanVisuals(n);
    }

    // Bounds monde calculés à partir des données statiques du mesh (sharedMesh.bounds), en
    // transformant les 8 coins via la hiérarchie de Transform réelle. Contrairement à
    // Renderer.bounds (potentiellement pas à jour pour un SkinnedMeshRenderer venant d'être
    // instancié), sharedMesh.bounds + TransformPoint est toujours immédiatement correct.
    static Bounds? ComputeRendererWorldBounds(Transform root)
    {
        var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh != null)
            return TransformLocalBounds(smr.transform, smr.sharedMesh.bounds);

        var mf = root.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            return TransformLocalBounds(mf.transform, mf.sharedMesh.bounds);

        return null;
    }

    static Bounds TransformLocalBounds(Transform t, Bounds local)
    {
        Vector3 c = local.center;
        Vector3 e = local.extents;
        Bounds result = new Bounds(t.TransformPoint(c), Vector3.zero);
        for (int xi = -1; xi <= 1; xi += 2)
            for (int yi = -1; yi <= 1; yi += 2)
                for (int zi = -1; zi <= 1; zi += 2)
                    result.Encapsulate(t.TransformPoint(c + new Vector3(e.x * xi, e.y * yi, e.z * zi)));
        return result;
    }

    void RebuildHumanVisuals(int n)
    {
        if (kickRingTransforms != null)
            foreach (var t in kickRingTransforms)
                if (t != null) Destroy(t.gameObject);
        if (arrowTransforms != null)
            foreach (var arr in arrowTransforms)
                if (arr != null) Destroy(arr.gameObject);

        kickRingTransforms = new Transform[n];
        kickRingMaterials = new Material[n];
        haloTimers = new float[n];
        arrowTransforms = new Transform[n];

        for (int i = 0; i < n; i++)
        {
            if (humanPlayerSet == null || !humanPlayerSet.Contains(i)) continue;
            kickRingTransforms[i] = CreateKickRing(i, playerTransforms[i]);
            arrowTransforms[i] = CreateArrow(i);
        }
    }

    static Mesh GetGroundQuadMesh()
    {
        if (groundQuadMesh != null) return groundQuadMesh;
        groundQuadMesh = new Mesh { name = "GroundQuad" };
        groundQuadMesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
        };
        groundQuadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        groundQuadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        groundQuadMesh.RecalculateNormals();
        groundQuadMesh.RecalculateBounds();
        return groundQuadMesh;
    }

    static Mesh GetBillboardQuadMesh()
    {
        if (billboardQuadMesh != null) return billboardQuadMesh;
        billboardQuadMesh = new Mesh { name = "BillboardQuad" };
        billboardQuadMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
        };
        billboardQuadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        billboardQuadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        billboardQuadMesh.RecalculateNormals();
        billboardQuadMesh.RecalculateBounds();
        return billboardQuadMesh;
    }

    // Anneau de tir : PAS parenté au joueur (position resynchronisée manuellement dans
    // UpdateKickRing, comme la flèche). Un enfant hériterait de l'échelle du Transform du
    // joueur, or celle-ci est animée pendant le kick (pulse jusqu'à kickScalePulse en
    // 0.3s, cf ApplyPlayer) : l'anneau gonflait alors bien au-delà de son rayon réel et son
    // animation se retrouvait mélangée à celle, plus courte, du pulse du joueur.
    // Dessiné par HumanRingIndicator.shader ; l'échelle du quad est fixée une fois pour
    // couvrir le rayon max de tir, _Radius (0-0.5, fraction du quad) est piloté depuis
    // UpdateKickRing pour l'animation d'expansion.
    Transform CreateKickRing(int index, Transform parent)
    {
        var go = new GameObject($"HumanKickRing_{index}");
        go.transform.position = parent.position + Vector3.up * haloHeight;
        go.transform.rotation = Quaternion.identity;

        float diameter = RustKickRadius * worldScale * 2f;
        go.transform.localScale = new Vector3(diameter, 1f, diameter);

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = GetGroundQuadMesh();
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Custom/HumanRingIndicator"));
        mat.color = haloColor;
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        kickRingMaterials[index] = mat;
        go.SetActive(false);
        return go.transform;
    }

    // Flèche façon FIFA : pas parentée au joueur (doit toujours faire face à la
    // caméra, indépendamment de la rotation du joueur), dessinée par
    // HumanArrowIndicator.shader. Taille/hauteur dérivées des bounds détectés du
    // prefab (playerWidths/playerHeights) plutôt que de valeurs fixes.
    Transform CreateArrow(int index)
    {
        var go = new GameObject($"HumanArrow_{index}");
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = GetBillboardQuadMesh();
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Custom/HumanArrowIndicator"));
        mat.color = arrowColor;
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        float width = (playerWidths != null && index < playerWidths.Length) ? playerWidths[index] : 1f;
        float size = Mathf.Max(width * arrowSize, 0.05f);
        go.transform.localScale = new Vector3(10, 10, 1f);

        return go.transform;
    }

    Vector3 ToWorld(float x, float y)
    {
        float wx = (x - fieldWidth * 0.5f) * worldScale;
        float wz = (y - fieldHeight * 0.5f) * worldScale;
        return new Vector3(wx, 0f, wz);
    }

    void HandleWorldState(WorldState state)
    {
        // Debug.Log($"[GameStateReceiver] frame reçue, tick={state.tick}, players={state.players?.Length}");
        Debug.Log($"[GameStateReceiver] frame reçue, tick={state.tick}, players={state.players?.Length}, score={state.team0_score}-{state.team1_score}");
        EnsureBallSpawned();
        WorldState prevForEdges = currentState;
        previousState = currentState;
        currentState = state;
        lastReceiveTime = Time.realtimeSinceStartup;

        ScoreManager.Instance.SetScore(state.team0_score, state.team1_score);

        DetectPlayerTriggers(prevForEdges, state);
    }

    // Détection des fronts montants (début de kick / dash) à chaque message reçu, plutôt que
    // dans Update(). Le serveur peut envoyer plusieurs world_state par frame Unity (simulation
    // plus rapide que le rendu) ; comme GameNetworkClient dépile tous les messages en attente
    // à chaque frame, une détection dans Update() ne voit que la dernière paire prev/current du
    // paquet et rate le front montant si un kick démarre ET se termine dans le même paquet.
    // En détectant ici, sur CHAQUE message reçu, aucun front n'est jamais raté.
    void DetectPlayerTriggers(WorldState prev, WorldState cur)
    {
        if (cur?.players == null) return;
        if (kickPulseTimers == null || kickPulseTimers.Length != cur.players.Length) return;

        for (int i = 0; i < cur.players.Length; i++)
        {
            bool wasKicking = prev?.players != null && i < prev.players.Length && prev.players[i].kick_timer != 0;
            bool isKicking = cur.players[i].kick_timer != 0;
            if (isKicking && !wasKicking)
            {
                kickPulseTimers[i] = kickPulseDuration;
                if (playerAnimators != null && i < playerAnimators.Length && playerAnimators[i] != null)
                    playerAnimators[i].SetTrigger("Kick");
                if (humanPlayerSet != null && humanPlayerSet.Contains(i))
                    haloTimers[i] = haloDuration;
            }

            bool wasDashing = prev?.players != null && i < prev.players.Length && prev.players[i].dash_timer != 0;
            bool isDashing = cur.players[i].dash_timer != 0;
            if (isDashing && !wasDashing)
                dashStretchTimers[i] = dashStretchDuration;
        }
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
        }

        // Les triggers (kick pulse, halo, dash) sont désormais détectés message par message
        // dans DetectPlayerTriggers, pas ici : voir HandleWorldState.

        UpdateKickRing(index, tr.position);
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

    void UpdateKickRing(int index, Vector3 playerWorldPos)
    {
        Transform ringTr = kickRingTransforms[index];
        if (ringTr == null) return;
        Material mat = kickRingMaterials[index];

        ringTr.position = playerWorldPos + Vector3.up * haloHeight;

        if (haloTimers[index] > 0f)
        {
            haloTimers[index] -= Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(haloTimers[index] / haloDuration); // 0 (début) -> 1 (fin)

            float startRadius = RustPlayerRadius * worldScale * haloStartRadiusMultiplier;
            float endRadius = RustKickRadius * worldScale;
            float radiusWorld = Mathf.Lerp(startRadius, endRadius, progress);
            float radiusUv = radiusWorld / (endRadius * 2f);

            if (!ringTr.gameObject.activeSelf) ringTr.gameObject.SetActive(true);
            mat.SetFloat("_Radius", radiusUv);
            mat.SetFloat("_Thickness", haloLineWidth);
            mat.SetFloat("_Alpha", 1f - progress);
        }
        else if (ringTr.gameObject.activeSelf)
        {
            ringTr.gameObject.SetActive(false);
        }
    }

    void UpdateHumanArrow(int index, Vector3 playerWorldPos)
    {
        Transform arrowTr = arrowTransforms[index];
        if (arrowTr == null) return;

        float height = (playerHeights != null && index < playerHeights.Length) ? playerHeights[index] + 16 : 2f;
        float bob = Mathf.Sin(Time.time * arrowBobSpeed) * arrowBobAmplitude;
        arrowTr.position = playerWorldPos + Vector3.up * (height + arrowHeightAbovePlayer + bob);

        Camera cam = Camera.main;
        if (cam != null)
            arrowTr.rotation = Quaternion.LookRotation(arrowTr.position - cam.transform.position, Vector3.up);
    }
}
