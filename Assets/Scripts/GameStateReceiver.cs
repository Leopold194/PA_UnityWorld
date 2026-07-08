using System.Net.Sockets;
using System.Text;
using UnityEngine;


[System.Serializable]
public class WorldState
{
    public ulong tick;
    public EntityState ball;
    public PlayerState[] players;
}

[System.Serializable]
public class EntityState
{
    public float x, y, vx, vy;
}

[System.Serializable]
public class PlayerState
{
    public float x, y, vx, vy, look_x, look_y;
    public byte dash_timer, kick_timer, team;
}

public class GameStateReceiver : MonoBehaviour
{
    [Header("Prefabs à assigner dans l'inspecteur")]
    public Transform ballPrefab;
    public Transform playerPrefab;

    [Header("Mapping terrain")]
    public float fieldWidth = 2500f;
    public float fieldHeight = 1400f;
    public float worldScale = 0.01f;

    [Header("Doit matcher tick_duration côté Rust")]
    public float receiveInterval = 1f / 60f;

    TcpClient client;
    NetworkStream stream;
    byte[] lengthBuf = new byte[4];

    object lockObj = new object();
    WorldState pendingState;   // dernier paquet reçu par le thread réseau, pas encore consommé
    ulong lastConsumedTick = ulong.MaxValue;

    WorldState currentState;
    WorldState previousState;
    float lastReceiveTime;

    Transform ballTransform;
    Transform[] playerTransforms;

    void Start()
    {
        client = new TcpClient("127.0.0.1", 7777);
        client.NoDelay = true;
        stream = client.GetStream();
        System.Threading.ThreadPool.QueueUserWorkItem(_ => ReceiveLoop());

        ballTransform = Instantiate(ballPrefab);
        ballTransform.name = "Ball";
    }

    void ReceiveLoop()
    {
        try
        {
            while (true)
            {
                if (!ReadExact(lengthBuf, 4)) break;
                int len = System.BitConverter.ToInt32(lengthBuf, 0);
                byte[] payload = new byte[len];
                if (!ReadExact(payload, len)) break;

                string json = Encoding.UTF8.GetString(payload);
                var state = JsonUtility.FromJson<WorldState>(json);

                // AUCUN appel à une API Unity ici (Time, GameObject, etc.)
                lock (lockObj) { pendingState = state; }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ReceiveLoop crash: {e}");
        }
    }

    bool ReadExact(byte[] buf, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buf, read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    void EnsurePlayersSpawned(int n)
    {
        if (playerTransforms != null && playerTransforms.Length == n) return;

        if (playerTransforms != null)
            foreach (var t in playerTransforms)
                if (t != null) Destroy(t.gameObject);

        playerTransforms = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            playerTransforms[i] = Instantiate(playerPrefab);
            playerTransforms[i].name = $"Player_{i}";
        }
    }

    Vector3 ToWorld(float x, float y)
    {
        float wx = (x - fieldWidth * 0.5f) * worldScale;
        float wz = (y - fieldHeight * 0.5f) * worldScale;
        return new Vector3(wx, 0f, wz);
    }

    void Update()
    {
        // Récupère (et consomme) le dernier paquet reçu, sur le thread principal
        WorldState fresh = null;
        lock (lockObj)
        {
            if (pendingState != null && pendingState.tick != lastConsumedTick)
            {
                fresh = pendingState;
            }
        }

        if (fresh != null)
        {
            previousState = currentState;
            currentState = fresh;
            lastConsumedTick = fresh.tick;
            lastReceiveTime = Time.realtimeSinceStartup; // OK ici : thread principal
        }

        if (currentState == null) return;

        EnsurePlayersSpawned(currentState.players.Length);

        float t = previousState == null
            ? 1f
            : Mathf.Clamp01((Time.realtimeSinceStartup - lastReceiveTime) / receiveInterval);

        ApplyBall(currentState.ball, previousState?.ball, t);

        for (int i = 0; i < currentState.players.Length; i++)
        {
            PlayerState prev = (previousState != null && i < previousState.players.Length)
                ? previousState.players[i] : null;
            ApplyPlayer(playerTransforms[i], currentState.players[i], prev, t);
        }
    }

    void ApplyBall(EntityState cur, EntityState prev, float t)
    {
        Vector3 target = ToWorld(cur.x, cur.y);
        ballTransform.position = prev != null
            ? Vector3.Lerp(ToWorld(prev.x, prev.y), target, t)
            : target;
    }

    void ApplyPlayer(Transform tr, PlayerState cur, PlayerState prev, float t)
    {
        Vector3 target = ToWorld(cur.x, cur.y);
        tr.position = prev != null ? Vector3.Lerp(ToWorld(prev.x, prev.y), target, t) : target;

        Vector3 lookDir = new Vector3(cur.look_x, 0f, cur.look_y);
        if (lookDir.sqrMagnitude > 1e-4f)
            tr.rotation = Quaternion.LookRotation(lookDir);

        var renderer = tr.GetComponentInChildren<Renderer>();
        if (renderer != null)
            renderer.material.color = cur.team == 0 ? Color.blue : Color.red;
    }

    void OnDestroy()
    {
        stream?.Close();
        client?.Close();
    }
}