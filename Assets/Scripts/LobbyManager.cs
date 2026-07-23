using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class LobbyManager : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelLobby;
    public Transform playerListContainer;   // parent des lignes de joueurs (VerticalLayoutGroup)
    public GameObject playerRowPrefab;      // prefab avec un Text pour l'id/équipe/statut
    public Button buttonTeamA;
    public Button buttonTeamB;
    public Button buttonReady;
    public GameObject scorePanel;

    [Header("Suite du flow")]
    public GameObject menuCanvas;
    public GamepadInputSender inputSender;  // cf. section 6
    public GameStateReceiver gameStateReceiver;
    public UIManager uiManager;

    int myClientId = -1;
    int myTeam = -1;

    void OnEnable()
    {
        GameNetworkClient.Instance.OnLobbyState += HandleLobbyState;
        GameNetworkClient.Instance.OnGameStart += HandleGameStart;
    }

    void OnDisable()
    {
        if (GameNetworkClient.Instance == null) return;
        GameNetworkClient.Instance.OnLobbyState -= HandleLobbyState;
        GameNetworkClient.Instance.OnGameStart -= HandleGameStart;
    }

    public void EnterLobby()
    {
        panelLobby.SetActive(true);
        GameNetworkClient.Instance.SendJoinLobby();
    }

    public void ChoisirEquipeA()
    {
        myTeam = 0;
        GameNetworkClient.Instance.SendSelectTeam(0);
    }

    public void ChoisirEquipeB()
    {
        myTeam = 1;
        GameNetworkClient.Instance.SendSelectTeam(1);
    }

    public void CliquerPret()
    {   
        Debug.Log($"CliquerPret: myTeam={myTeam}");
        if (myTeam == -1)
        {
            Debug.LogWarning("Choisissez une équipe avant de vous déclarer prêt.");
            return;
        }
        GameNetworkClient.Instance.SendReady();
    }

    void HandleLobbyState(LobbyStateMsg state)
    {
        myClientId = state.your_client_id;
        RefreshPlayerList(state.players);
    }

    void RefreshPlayerList(LobbyPlayerInfo[] players)
    {
        foreach (Transform child in playerListContainer) Destroy(child.gameObject);

        foreach (var p in players)
        {
            var row = Instantiate(playerRowPrefab, playerListContainer);
            row.SetActive(true);
            string teamLabel = p.team == -1 ? "?" : (p.team == 0 ? "Équipe A" : "Équipe B");
            string readyLabel = p.ready ? "Prêt" : "En attente";
            string me = p.client_id == myClientId ? " (vous)" : "";

            var text = row.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = $"Joueur {p.client_id}{me} — {teamLabel} — {readyLabel}";
            else Debug.LogWarning("playerRowPrefab n'a pas de composant TMP_Text (ou Text) dans ses enfants.");
        }
    }

    void HandleGameStart(GameStartMsg msg)
    {
        Debug.Log("HandleGameStart: le jeu commence !");
        panelLobby.SetActive(false);
        menuCanvas.SetActive(false);
        scorePanel.SetActive(true);
        if (inputSender != null) inputSender.enabled = true;
    }

    // Quitte la partie en cours et revient au menu principal : coupe la connexion au
    // serveur (le protocole n'a pas de message "leave", donc on ferme simplement la
    // socket) et détruit les entités de jeu instanciées, pour repartir sur un état propre
    // si le joueur relance une partie ensuite.
    public void QuitToMenu()
    {
        if (inputSender != null) inputSender.enabled = false;
        gameStateReceiver?.ResetGame();
        GameNetworkClient.Instance.Disconnect();

        myClientId = -1;
        myTeam = -1;
        Debug.Log("QuitToMenu: déconnexion du serveur et retour au menu principal.");
        uiManager.ShowMainMenu();
    }
}