using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LambdaLauncher : MonoBehaviour
{
    [Header("Configuration Lambda")]
    public string lambdaUrl = "https://a7bokfppcxhgywpl7dhswglfwa0ivucj.lambda-url.eu-west-3.on.aws/";
    public string weightsUrl = "https://football-infer-weights-747554529591.s3.eu-west-3.amazonaws.com/weights/distill_best.mpk";
    public string region = "eu-west-3";
    public string service = "lambda";

    [Header("Clés AWS")]
    public string accessKey = "";
    public string secretKey = "";

    [Header("Serveur local (debug)")]
    // TODO: remettre à false pour repasser sur AWS Lambda une fois le test local terminé.
    public bool useLocalServer = false;
    public string localServerIp = "127.0.0.1";
    public int localServerPort = 7777;

    [Header("Références scène")]
    public GameStateReceiver receiver;
    public GameObject menuCanvas;
    public GameObject canvasScore;
    public GameObject panelChargement;
    public GameObject panelMainMenu;
    
    public LobbyManager lobbyManager;

    public void LancerJoueur()
    {
        GameChoice.Selected = GameChoice.Mode.Jouer;
        StartCoroutine(LaunchCoroutine());
    }

    public void LancerVisualisation()
    {
        GameChoice.Selected = GameChoice.Mode.Visualisation;
        StartCoroutine(LaunchCoroutine());
    }

    IEnumerator LaunchCoroutine()
    {
        panelMainMenu.SetActive(false);
        panelChargement.SetActive(true);

        if (useLocalServer)
        {
            // Mode debug : le serveur tourne déjà en local (docker run ... -p 7777:7777),
            // donc on se connecte directement dessus, sans passer par la Lambda AWS.
            Debug.Log($"[LambdaLauncher] Mode serveur local actif -> connexion à {localServerIp}:{localServerPort}");
            GameNetworkClient.Instance.Connect(localServerIp, localServerPort);
        }
        else
        {
            // ---- Appel AWS Lambda (désactivé pendant les tests en local) ----
            string body = JsonUtility.ToJson(new WeightsRequest { model_weights_url = weightsUrl, mode = GameChoice.Selected.ToString().ToLower() });
            Debug.Log($"[LambdaLauncher] Appel Lambda : {lambdaUrl} avec body={body}");
            Uri uri = new Uri(lambdaUrl);
            string authHeader = AwsSigV4.Sign("POST", uri, body, accessKey, secretKey,
                region, service, out string amzDate, out string payloadHash);

            using var req = new UnityWebRequest(lambdaUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-amz-date", amzDate);
            req.SetRequestHeader("x-amz-content-sha256", payloadHash);
            req.SetRequestHeader("Authorization", authHeader);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Erreur Lambda : {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            var response = JsonUtility.FromJson<LambdaResponse>(req.downloadHandler.text);
            Debug.Log($"Serveur prêt : {response.ip}:{response.port} (task {response.taskArn})");

            GameNetworkClient.Instance.Connect(response.ip, response.port);
            // ---- Fin appel AWS Lambda ----
        }

        panelChargement.SetActive(false);

        if (GameChoice.Selected == GameChoice.Mode.Jouer)
        {
            lobbyManager.EnterLobby();
        }
        else
        {
            menuCanvas.SetActive(false);
            canvasScore.SetActive(true);
        }       
    }

    [Serializable] class WeightsRequest { public string model_weights_url; public string mode; }
    [Serializable] class LambdaResponse { public string ip; public int port; public string taskArn; }
}