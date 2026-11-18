using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using GuidantShare.Core;

namespace GuidantShare.ShareFile;

/// <summary>
/// ShareFile REST v3 client using the wrapper's OWN OAuth app. Credentials and the refresh token are
/// stored encrypted via ISecretStore and shared by the whole team (one connection, set by a SuperAdmin).
/// </summary>
public class ShareFileService : IShareFileService
{
    private const string KSub = "sharefile.subdomain", KId = "sharefile.client_id", KSecret = "sharefile.client_secret",
        KRedirect = "sharefile.redirect_uri", KRefresh = "sharefile.refresh_token", KBy = "sharefile.connected_by",
        KWhen = "sharefile.connected_utc";

    private readonly ISecretStore _secrets;
    private readonly HttpClient _http;
    private readonly ILogger<ShareFileService> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _access; private DateTime _accessExp; private string? _lastError;

    public ShareFileService(ISecretStore secrets, HttpClient http, ILogger<ShareFileService> log)
    {
        _secrets = secrets; _http = http; _log = log;
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    private static string Host(string sub) => $"https://{sub}.sharefile.com";
    private static string Api(string sub) => $"https://{sub}.sf-api.com/sf/v3";

    public async Task<ShareFileStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var sub = await _secrets.GetAsync(KSub, ct);
        var id = await _secrets.GetAsync(KId, ct);
        var sec = await _secrets.GetAsync(KSecret, ct);
        var refresh = await _secrets.GetAsync(KRefresh, ct);
        var when = await _secrets.GetAsync(KWhen, ct);
        return new ShareFileStatus(
            Configured: !string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(sec),
            Connected: !string.IsNullOrEmpty(refresh), Subdomain: sub, ConnectedBy: await _secrets.GetAsync(KBy, ct),
            ConnectedUtc: DateTime.TryParse(when, null, System.Globalization.DateTimeStyles.RoundtripKind, out var w) ? w : null,
            Error: _lastError, RedirectUri: await _secrets.GetAsync(KRedirect, ct), ClientId: id);
    }

    public async Task SaveAppSettingsAsync(string subdomain, string clientId, string? clientSecret, string? redirectUri, CancellationToken ct = default)
    {
        await _secrets.SetAsync(KSub, subdomain.Trim().ToLowerInvariant(), ct);
        await _secrets.SetAsync(KId, clientId.Trim(), ct);
        if (!string.IsNullOrWhiteSpace(clientSecret)) await _secrets.SetAsync(KSecret, clientSecret.Trim(), ct);
        if (!string.IsNullOrWhiteSpace(redirectUri)) await _secrets.SetAsync(KRedirect, redirectUri.Trim(), ct);
        _access = null;
    }

    public async Task<string> BuildAuthorizeUrlAsync(string state, CancellationToken ct = default)
    {
        var (sub, id, _, redirect) = await GetAppAsync(ct);
        return $"{Host(sub)}/oauth/authorize?response_type=code&client_id={Uri.EscapeDataString(id)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirect)}&state={Uri.EscapeDataString(state)}";
    }

    private async Task<(string sub, string id, string secret, string redirect)> GetAppAsync(CancellationToken ct)
    {
        var sub = await _secrets.GetAsync(KSub, ct); var id = await _secrets.GetAsync(KId, ct);
        var sec = await _secrets.GetAsync(KSecret, ct); var red = await _secrets.GetAsync(KRedirect, ct);
        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(sec))
            throw new InvalidOperationException("ShareFile app settings (subdomain, client id, client secret) are not saved yet.");
        return (sub, id, sec, red ?? "https://secure.sharefile.com/oauth/oauthcomplete.aspx");
    }

    public async Task ConnectWithCodeAsync(string code, string connectedBy, CancellationToken ct = default)
    {
        var (sub, id, secret, redirect) = await GetAppAsync(ct);
        var tok = await PostTokenAsync(sub, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = id,
            ["client_secret"] = secret, ["redirect_uri"] = redirect
        }, ct);
        await _secrets.SetAsync(KRefresh, tok.refresh!, ct);
        await _secrets.SetAsync(KBy, connectedBy, ct);
        await _secrets.SetAsync(KWhen, DateTime.UtcNow.ToString("O"), ct);
        _access = tok.access; _accessExp = DateTime.UtcNow.AddSeconds(tok.expires - 900); _lastError = null;
        _log.LogInformation("ShareFile connected by {User}", connectedBy);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _secrets.DeleteAsync(KRefresh, ct); await _secrets.DeleteAsync(KBy, ct); await _secrets.DeleteAsync(KWhen, ct);
        _access = null;
    }

    private async Task<(string access, string? refresh, int expires)> PostTokenAsync(string sub, Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await _http.PostAsync($"{Host(sub)}/oauth/token", new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("ShareFile token request failed: {Status}", (int)resp.StatusCode);
            throw new InvalidOperationException($"ShareFile rejected the request ({(int)resp.StatusCode}). " + Trim(body));
        }
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        return (r.GetProperty("access_token").GetString()!,
            r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            r.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 28800);
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;

    private async Task<(string token, string sub)> GetTokenAsync(CancellationToken ct, bool force = false)
    {
        var (sub, id, secret, _) = await GetAppAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            if (!force && _access != null && DateTime.UtcNow < _accessExp) return (_access, sub);
            var refresh = await _secrets.GetAsync(KRefresh, ct) ?? throw new ShareFileNotConnectedException();
            try
            {
                var tok = await PostTokenAsync(sub, new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = id, ["client_secret"] = secret
                }, ct);
                if (tok.refresh != null && tok.refresh != refresh) await _secrets.SetAsync(KRefresh, tok.refresh, ct);
                _access = tok.access; _accessExp = DateTime.UtcNow.AddSeconds(Math.Max(60, tok.expires - 900)); _lastError = null;
                return (_access, sub);
            }
            catch (Exception ex) when (ex is not ShareFileNotConnectedException)
            {
                _lastError = "ShareFile authorization failed; reconnect needed. " + ex.Message;
                throw;
            }
        }
        finally { _lock.Release(); }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> build, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (token, sub) = await GetTokenAsync(ct, force: false);
            var req = build(Api(sub));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req, ct); }
            catch (HttpRequestException) when (attempt < 3) { await Task.Delay(Backoff(attempt), ct); continue; }
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                resp.Dispose(); await GetTokenAsync(ct, force: true); continue;
            }
            if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
            {
                var snippet = await resp.Content.ReadAsStringAsync(ct);
                _log.LogWarning("ShareFile {Method} {Path} -> {Status} (attempt {Attempt}) {Body}", req.Method, req.RequestUri?.AbsolutePath, (int)resp.StatusCode, attempt + 1, Trim(snippet));
            }
            if ((resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500) && attempt < 3)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? Backoff(attempt);
                resp.Dispose(); await Task.Delay(wait, ct); continue;
            }
            return resp;
        }
    }

    private static TimeSpan Backoff(int n) => TimeSpan.FromSeconds(Math.Pow(2, n));

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage r, CancellationToken ct)
    {
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
            throw new HttpRequestException($"ShareFile returned {(int)r.StatusCode}. {Trim(body)}", null, r.StatusCode);
        return JsonDocument.Parse(body);
    }

    private static SfItem ToItem(JsonElement e)
    {
        var type = e.TryGetProperty("odata.type", out var t) ? t.GetString() ?? "" : "";
        DateTime? mod = null;
        if (e.TryGetProperty("ClientModifiedDate", out var cm) && cm.ValueKind == JsonValueKind.String && DateTime.TryParse(cm.GetString(), out var d1)) mod = d1.ToUniversalTime();
        else if (e.TryGetProperty("CreationDate", out var cd) && cd.ValueKind == JsonValueKind.String && DateTime.TryParse(cd.GetString(), out var d2)) mod = d2.ToUniversalTime();
        long size = e.TryGetProperty("FileSizeBytes", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : 0;
        return new SfItem(e.GetProperty("Id").GetString()!, e.GetProperty("Name").GetString() ?? "", size, mod, type.EndsWith("Folder", StringComparison.OrdinalIgnoreCase) || e.GetProperty("Id").GetString()!.StartsWith("fo", StringComparison.Ordinal));
    }

    public async Task<string?> ResolvePathAsync(string path, CancellationToken ct = default)
    {
        var p = "/" + path.Trim('/');
        using var resp = await SendAsync(api => new HttpRequestMessage(HttpMethod.Get, $"{api}/Items/ByPath?path={Uri.EscapeDataString(p)}"), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        using var doc = await ReadAsync(resp, ct);
        return doc.RootElement.GetProperty("Id").GetString();
    }

    public async Task<IReadOnlyList<SfItem>> ListChildrenAsync(string folderId, CancellationToken ct = default)
    {
        var all = new List<SfItem>();
        string? next = null;
        do
        {
            var n = next;
            using var resp = await SendAsync(api => new HttpRequestMessage(HttpMethod.Get,
                n ?? $"{api}/Items({folderId})/Children?$top=1000"), ct);
            using var doc = await ReadAsync(resp, ct);
            foreach (var e in doc.RootElement.GetProperty("value").EnumerateArray()) all.Add(ToItem(e));
            next = doc.RootElement.TryGetProperty("odata.nextLink", out var nl) ? nl.GetString() : null;
        } while (!string.IsNullOrEmpty(next));
        return all;
    }

    public async Task<SfItem?> GetItemAsync(string id, CancellationToken ct = default)
    {
        using var resp = await SendAsync(api => new HttpRequestMessage(HttpMethod.Get, $"{api}/Items({id})"), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        using var doc = await ReadAsync(resp, ct);
        return ToItem(doc.RootElement);
    }

    public async Task<SfItem> UploadAsync(string folderId, string fileName, Stream content, long length, bool overwrite, CancellationToken ct = default)
    {
        // Same method FileSync uses: 1) negotiate with Upload?raw=true, 2) POST the raw bytes to the returned ChunkUri.
        var q = $"raw=true&fileName={Uri.EscapeDataString(fileName)}" + (overwrite ? "&overwrite=true" : "");
        using var init = await SendAsync(api => new HttpRequestMessage(HttpMethod.Post, $"{api}/Items({folderId})/Upload?{q}"), ct);
        if (!init.IsSuccessStatusCode)
            throw new HttpRequestException($"ShareFile refused to start the upload ({(int)init.StatusCode}). Check that the connected account can upload to this folder.");
        using var initDoc = await ReadAsync(init, ct);
        var chunkUri = initDoc.RootElement.GetProperty("ChunkUri").GetString()!;

        var raw = new StreamContent(content);
        raw.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var up = new HttpRequestMessage(HttpMethod.Post, chunkUri) { Content = raw };
        using var upResp = await _http.SendAsync(up, ct);
        var upBody = await upResp.Content.ReadAsStringAsync(ct);
        if (!upResp.IsSuccessStatusCode)
        {
            _log.LogWarning("ShareFile chunk upload -> {Status} {Body}", (int)upResp.StatusCode, Trim(upBody));
            throw new HttpRequestException($"ShareFile accepted the upload request but failed while receiving the file ({(int)upResp.StatusCode}).");
        }

        // Find the new item by name in the folder (newest match).
        var items = await ListChildrenAsync(folderId, ct);
        return items.Where(i => !i.IsFolder && i.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.Modified).FirstOrDefault()
               ?? throw new InvalidOperationException("Upload finished but the file was not found in the folder.");
    }

    /// <summary>Web-app link. ShareFile's web UI opens folders by id; files are opened via their containing folder.</summary>
    public async Task<string> GetWebLinkAsync(string folderId, CancellationToken ct = default)
    {
        var sub = await _secrets.GetAsync(KSub, ct);
        return $"https://{sub}.sharefile.com/home/shared/{folderId}";
    }
}
