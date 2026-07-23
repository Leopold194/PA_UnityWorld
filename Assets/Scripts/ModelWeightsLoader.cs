using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

[Serializable]
public class WeightEntry
{
    public string filename;
    public string url;
    public long size;
    public string last_modified;
}

[Serializable]
public class WeightsResponse
{
    public WeightEntry[] weights;
}


public class ModelWeightsLoader : MonoBehaviour
{
    [Header("UI - assigner dans l'inspecteur")]
    public TMP_Dropdown modelDropdown;
    public TMP_Dropdown checkpointDropdown;

    [Header("Config Lambda / AWS")]
    [SerializeField]
    private string lambdaBaseUrl = "https://a7bokfppcxhgywpl7dhswglfwa0ivucj.lambda-url.eu-west-3.on.aws/";
    [SerializeField] private string awsRegion = "eu-west-3";
    [SerializeField] private string awsService = "lambda";

    [SerializeField] private string accessKeyId = "";
    [SerializeField] private string secretAccessKey = "";

    private List<WeightEntry> allWeights = new List<WeightEntry>();
    private Dictionary<string, List<string>> checkpointsByModel = new Dictionary<string, List<string>>();

    private void Awake()
    {
        if (string.IsNullOrEmpty(accessKeyId))
            accessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        if (string.IsNullOrEmpty(secretAccessKey))
            secretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");

        if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
        {
            Debug.LogError("[ModelWeightsLoader] AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY manquants.");
        }
    }

    private void Start()
    {
        StartCoroutine(FetchWeightsAndFillDropdowns());
    }

    private IEnumerator FetchWeightsAndFillDropdowns()
    {
        string listUrl = lambdaBaseUrl.TrimEnd('/') + "/weights";
        Dictionary<string, string> headers = SignRequest("GET", listUrl, "");

        using (UnityWebRequest req = UnityWebRequest.Get(listUrl))
        {
            foreach (var kv in headers)
                req.SetRequestHeader(kv.Key, kv.Value);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ModelWeightsLoader] Erreur requête /weights : {req.error} - {req.downloadHandler?.text}");
                yield break;
            }

            WeightsResponse response = JsonUtility.FromJson<WeightsResponse>(req.downloadHandler.text);
            if (response == null || response.weights == null)
            {
                Debug.LogError("[ModelWeightsLoader] Réponse /weights vide ou invalide.");
                yield break;
            }

            PopulateDropdowns(response);
        }
    }

    private void PopulateDropdowns(WeightsResponse response)
    {
        allWeights = response.weights.ToList();
        checkpointsByModel.Clear();

        foreach (var w in allWeights)
        {
            (string modelName, string checkpoint) = ParseFilename(w.filename);
            if (string.IsNullOrEmpty(modelName)) continue;

            if (!checkpointsByModel.ContainsKey(modelName))
                checkpointsByModel[modelName] = new List<string>();

            if (!checkpointsByModel[modelName].Contains(checkpoint))
                checkpointsByModel[modelName].Add(checkpoint);
        }

        List<string> modelNames = checkpointsByModel.Keys.OrderBy(x => x).ToList();

        modelDropdown.ClearOptions();
        modelDropdown.AddOptions(modelNames);

        modelDropdown.onValueChanged.RemoveAllListeners();
        modelDropdown.onValueChanged.AddListener(OnModelChanged);

        if (modelNames.Count > 0)
            FillCheckpointDropdown(modelNames[0]);
    }

    private void OnModelChanged(int index)
    {
        string selectedModel = modelDropdown.options[index].text;
        FillCheckpointDropdown(selectedModel);
    }

    private void FillCheckpointDropdown(string modelName)
    {
        checkpointDropdown.ClearOptions();
        if (checkpointsByModel.TryGetValue(modelName, out var checkpoints))
        {
            List<string> sorted = checkpoints.OrderBy(x => x).ToList();
            checkpointDropdown.AddOptions(sorted);
        }
    }

    private (string modelName, string checkpoint) ParseFilename(string filename)
    {
        string nameNoExt = filename.Contains(".")
            ? filename.Substring(0, filename.LastIndexOf('.'))
            : filename;

        int lastUnderscore = nameNoExt.LastIndexOf('_');
        if (lastUnderscore < 0)
            return (nameNoExt, "");

        string modelName = nameNoExt.Substring(0, lastUnderscore);
        string checkpoint = nameNoExt.Substring(lastUnderscore + 1);
        return (modelName, checkpoint);
    }

    public string GetSelectedWeightUrl()
    {
        if (modelDropdown.options.Count == 0 || checkpointDropdown.options.Count == 0)
            return null;

        string model = modelDropdown.options[modelDropdown.value].text;
        string checkpoint = checkpointDropdown.options[checkpointDropdown.value].text;
        string filename = $"{model}_{checkpoint}.mpk";

        return allWeights.FirstOrDefault(w => w.filename == filename)?.url;
    }

    private Dictionary<string, string> SignRequest(string method, string url, string body)
    {
        var uri = new Uri(url);
        string host = uri.Host;
        string canonicalUri = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        string amzDate = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");

        string payloadHash = Sha256Hex(body ?? "");

        var headersToSign = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "content-type", "application/json" },
            { "host", host },
            { "x-amz-content-sha256", payloadHash },
            { "x-amz-date", amzDate }
        };

        string canonicalHeaders = string.Concat(headersToSign.Select(kv => $"{kv.Key}:{kv.Value}\n"));
        string signedHeaders = string.Join(";", headersToSign.Keys);

        string canonicalRequest = string.Join("\n", new[]
        {
            method,
            canonicalUri,
            "", // query string (vide ici)
            canonicalHeaders,
            signedHeaders,
            payloadHash
        });

        string credentialScope = $"{dateStamp}/{awsRegion}/{awsService}/aws4_request";
        string stringToSign = string.Join("\n", new[]
        {
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            Sha256Hex(canonicalRequest)
        });

        byte[] signingKey = GetSignatureKey(secretAccessKey, dateStamp, awsRegion, awsService);
        string signature = ToHex(HmacSha256(signingKey, stringToSign));

        string authorizationHeader =
            $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        return new Dictionary<string, string>
        {
            { "x-amz-date", amzDate },
            { "x-amz-content-sha256", payloadHash },
            { "Authorization", authorizationHeader },
            { "Content-Type", "application/json" }
        };
    }

    private static string Sha256Hex(string data)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return ToHex(hash);
        }
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using (var hmac = new HMACSHA256(key))
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        byte[] kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + key), dateStamp);
        byte[] kRegion = HmacSha256(kDate, regionName);
        byte[] kService = HmacSha256(kRegion, serviceName);
        return HmacSha256(kService, "aws4_request");
    }
}