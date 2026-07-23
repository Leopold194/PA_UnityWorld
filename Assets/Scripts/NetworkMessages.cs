using System;

[Serializable]
public class Envelope
{
    public string type;
    public string payload;
}

[Serializable]
public class LobbyPlayerInfo
{
    public int client_id;
    public int team;   // -1 = pas choisi, 0 ou 1
    public bool ready;
}

[Serializable]
public class LobbyStateMsg
{
    public int your_client_id;
    public LobbyPlayerInfo[] players;
}

[Serializable]
public class SelectTeamMsg
{
    public int team;
}

[Serializable]
public class GameStartMsg
{
    public int[] slots; // client_id par slot (indice = index joueur), -1 si bot
}

[Serializable]
public class PlayerInputMsg
{
    public float vx, vy;       // stick gauche : déplacement
    public float look_x, look_y; // stick droit : visée
    public bool kick, dash;
}