using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KSPClub
{
    /// <summary>
    /// Thin wrapper around the GitHub Contents API.
    /// All methods are coroutines — start them with StartCoroutine().
    /// Uses isNetworkError / isHttpError (Unity 2019.4 API).
    /// </summary>
    public class GitHubClient
    {
        private readonly string _token;
        private readonly string _owner;
        private readonly string _repo;

        private const string API = "https://api.github.com";

        public GitHubClient(string token, string owner, string repo)
        {
            _token = token;
            _owner = owner;
            _repo  = repo;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Get the git blob SHA for a file. Returns null on any error or 404.
        /// </summary>
        public IEnumerator GetSha(string path, Action<string?> callback)
        {
            using var req = Get($"{API}/repos/{_owner}/{_repo}/contents/{Uri.EscapeUriString(path)}");
            yield return req.SendWebRequest();

            if (req.isNetworkError || req.isHttpError)
            {
                if (req.responseCode != 404)
                    Debug.LogWarning($"[KSPClub] GetSha({path}): {req.error}");
                callback(null);
                yield break;
            }

            callback(ParseString(req.downloadHandler.text, "sha"));
        }

        /// <summary>
        /// Download a file's raw bytes. Returns null on error.
        /// Uses the download_url from the metadata response (works for private repos).
        /// </summary>
        public IEnumerator DownloadFile(string path, Action<byte[]?> callback)
        {
            // Step 1 — get metadata to find download_url
            using var meta = Get($"{API}/repos/{_owner}/{_repo}/contents/{Uri.EscapeUriString(path)}");
            yield return meta.SendWebRequest();

            if (meta.isNetworkError || meta.isHttpError)
            {
                Debug.LogWarning($"[KSPClub] DownloadFile metadata({path}): {meta.error}");
                callback(null);
                yield break;
            }

            // Step 2 — try inline base64 content first (files ≤ 1 MB)
            byte[]? content = DecodeContent(meta.downloadHandler.text);
            if (content != null) { callback(content); yield break; }

            // Step 3 — fall back to download_url for larger files
            string? dlUrl = ParseString(meta.downloadHandler.text, "download_url");
            if (dlUrl == null) { callback(null); yield break; }

            using var raw = Get(dlUrl);
            yield return raw.SendWebRequest();

            if (raw.isNetworkError || raw.isHttpError)
            {
                Debug.LogWarning($"[KSPClub] DownloadFile raw({path}): {raw.error}");
                callback(null);
                yield break;
            }

            callback(raw.downloadHandler.data);
        }

        /// <summary>
        /// Create or update a file. Pass currentSha=null to create new.
        /// Returns true on success (HTTP 200 or 201).
        /// </summary>
        public IEnumerator PutFile(
            string   path,
            byte[]   content,
            string   message,
            string?  currentSha,
            Action<bool> callback)
        {
            string b64  = Convert.ToBase64String(content);
            string body = currentSha != null
                ? $"{{\"message\":\"{Esc(message)}\",\"content\":\"{b64}\",\"sha\":\"{currentSha}\"}}"
                : $"{{\"message\":\"{Esc(message)}\",\"content\":\"{b64}\"}}";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            var req = new UnityWebRequest(
                $"{API}/repos/{_owner}/{_repo}/contents/{Uri.EscapeUriString(path)}", "PUT");
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 60;
            SetHeaders(req);
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            bool ok = !req.isNetworkError && !req.isHttpError;
            if (!ok)
                Debug.LogWarning($"[KSPClub] PutFile({path}): {req.error}\n{req.downloadHandler.text}");
            req.Dispose();
            callback(ok);
        }

        // ------------------------------------------------------------------ helpers

        private UnityWebRequest Get(string url)
        {
            var req = UnityWebRequest.Get(url);
            req.timeout = 20;
            SetHeaders(req);
            return req;
        }

        private void SetHeaders(UnityWebRequest req)
        {
            req.SetRequestHeader("Authorization", $"token {_token}");
            req.SetRequestHeader("User-Agent",    "KSPClubPlugin/0.1");
            req.SetRequestHeader("Accept",        "application/vnd.github.v3+json");
        }

        /// <summary>Extract a simple string field from GitHub JSON (no full parser needed).</summary>
        private static string? ParseString(string json, string field)
        {
            string key = $"\"{field}\":\"";
            int start  = json.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return null;
            start += key.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        /// <summary>
        /// Decode the base64 "content" field from a GitHub metadata response.
        /// GitHub wraps base64 at 60 chars with \n — strip those before decoding.
        /// Returns null if content field is absent or empty (file > 1 MB).
        /// </summary>
        private static byte[]? DecodeContent(string json)
        {
            const string key = "\"content\":\"";
            int start = json.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return null;
            start += key.Length;

            var sb = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') break;
                if (c == '\\' && i + 1 < json.Length) { i++; continue; } // skip \n etc.
                sb.Append(c);
            }

            string b64 = sb.ToString();
            if (string.IsNullOrEmpty(b64)) return null;

            try   { return Convert.FromBase64String(b64); }
            catch { return null; }
        }

        private static string Esc(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")
             .Replace("\n", "\\n").Replace("\r", "");
    }
}
