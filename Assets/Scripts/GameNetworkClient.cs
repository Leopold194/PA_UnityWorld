using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class GameNetworkClient : MonoBehaviour
{
    public static GameNetworkClient Instance { get; private set; }

    public event Action<LobbyStateMsg> OnLobbyState;
    public event Action<GameStartMsg> OnGameStart;
    public event Action<WorldState> OnWorldState;

    TcpClient client;
    NetworkStream stream;
    byte[] lengthBuf = new byte[4];
    bool running = false;

    // Les événements réseau arrivent sur un thread à part : on les met en file
    // et on les redéclenche sur le thread principal dans Update().
    ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Connect(string ip, int port)
    {
        if (running) return;
        client = new TcpClient(ip, port) { NoDelay = true };
        stream = client.GetStream();
        running = true;
        System.Threading.ThreadPool.QueueUserWorkItem(_ => ReceiveLoop());
    }

    void ReceiveLoop()
    {
        try
        {
            while (running)
            {
                if (!ReadExact(lengthBuf, 4)) break;
                int len = BitConverter.ToInt32(lengthBuf, 0);
                byte[] payload = new byte[len];
                if (!ReadExact(payload, len)) break;

                string json = Encoding.UTF8.GetString(payload);
                var envelope = JsonUtility.FromJson<Envelope>(json);
                Dispatch(envelope);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ReceiveLoop crash: {e}");
        }
    }

    void Dispatch(Envelope env)
    {
        switch (env.type)
        {
            case "lobby_state":
                var lobby = JsonUtility.FromJson<LobbyStateMsg>(env.payload);
                mainThreadActions.Enqueue(() => OnLobbyState?.Invoke(lobby));
                break;

            case "game_start":
                var gameStart = JsonUtility.FromJson<GameStartMsg>(env.payload);
                mainThreadActions.Enqueue(() => OnGameStart?.Invoke(gameStart));
                break;

            case "world_state":
                var world = JsonUtility.FromJson<WorldState>(env.payload);
                mainThreadActions.Enqueue(() => OnWorldState?.Invoke(world));
                break;

            default:
                if (env.type != null) Debug.LogWarning($"Type de message inconnu reçu : {env.type}");
                break;
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

    void Update()
    {
        while (mainThreadActions.TryDequeue(out var action)) action();
    }

    // ---- Envoi ----

    void Send(string type, string payloadJson)
    {   
        Debug.Log($"Envoi {type} : {payloadJson}");
        if (stream == null) return;
        var env = new Envelope { type = type, payload = payloadJson };
        string json = JsonUtility.ToJson(env);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] length = BitConverter.GetBytes(payload.Length);

        try
        {
            lock (this)
            {
                stream.Write(length, 0, 4);
                stream.Write(payload, 0, payload.Length);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Erreur d'envoi ({type}) : {e}");
        }
    }

    public void SendJoinLobby() => Send("join_lobby", "{}");
    public void SendSelectTeam(int team) => Send("select_team", JsonUtility.ToJson(new SelectTeamMsg { team = team }));
    public void SendReady() => Send("ready", "{}");
    public void SendInput(PlayerInputMsg msg) => Send("input", JsonUtility.ToJson(msg));

    void OnDestroy()
    {
        running = false;
        stream?.Close();
        client?.Close();
    }
}