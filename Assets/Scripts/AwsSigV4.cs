using System;
using System.Security.Cryptography;
using System.Text;

public static class AwsSigV4
{
    public static string Sign(
        string method, Uri uri, string body,
        string accessKey, string secretKey,
        string region, string service,
        out string amzDate, out string payloadHash)
    {
        var now = DateTime.UtcNow;
        amzDate = now.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = now.ToString("yyyyMMdd");

        payloadHash = Sha256Hex(body);
        string host = uri.Host;
        string canonicalUri = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;

        string canonicalHeaders =
            $"content-type:application/json\n" +
            $"host:{host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";
        string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";

        string canonicalRequest = string.Join("\n",
            method, canonicalUri, "", canonicalHeaders, signedHeaders, payloadHash);

        string credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        string stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256", amzDate, credentialScope, Sha256Hex(canonicalRequest));

        byte[] signingKey = GetSignatureKey(secretKey, dateStamp, region, service);
        string signature = ToHex(HmacSha256(signingKey, stringToSign));

        return $"AWS4-HMAC-SHA256 Credential={accessKey}/{credentialScope}, " +
               $"SignedHeaders={signedHeaders}, Signature={signature}";
    }

    static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    static byte[] GetSignatureKey(string key, string dateStamp, string region, string service)
    {
        byte[] kSecret = Encoding.UTF8.GetBytes("AWS4" + key);
        byte[] kDate = HmacSha256(kSecret, dateStamp);
        byte[] kRegion = HmacSha256(kDate, region);
        byte[] kService = HmacSha256(kRegion, service);
        return HmacSha256(kService, "aws4_request");
    }

    static string Sha256Hex(string data)
    {
        using var sha256 = SHA256.Create();
        return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}