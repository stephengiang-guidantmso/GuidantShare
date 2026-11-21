using System.Net.Http.Json;
using System.Text.Json;
using GuidantShare.Core;

namespace GuidantShare.Api.Services;

public class FileSyncOptions
{
    /// <summary>FileSync's private API. It listens on this machine only and is never reachable from the internet.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";
    /// <summary>Must equal FileSync's Api:Key. Set it in an environment variable (FileSync__ApiKey) or appsettings.Local.json, never in git.</summary>
    public string? ApiKey { get; set; }
    /// <summary>How long a catalog read is reused before FileSync is asked again.</summary>
    public int CacheSeconds { get; set; } = 45;
}

/// <summary>Talks to FileSync's localhost API with the API key. Keeps the last good catalog so a short FileSync outage does not take the site down.</summary>
public class FileSyncClient : IFileSyncClient
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TimeSpan _ttl;
    private readonly ILogger<FileSyncClient> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CatalogSnapshot? _good;
    private DateTime _nextTryUtc = DateTime.MinValue;
    private string? _lastError;

    public FileSyncClient(HttpClient http, FileSyncOptions options, ILogger<FileSyncClient> log)
    {
        _http = http;
        _log = log;
        _ttl = TimeSpan.FromSeconds(Math.Max(1, options.CacheSeconds));
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(60);
        if (!string.IsNullOrWhiteSpace(options.ApiKey)) _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", options.ApiKey);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default) =>
        await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

    public async Task<CatalogSnapshot> GetCatalogAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (!refresh && IsFresh(_good)) return _good!;

        await _lock.WaitAsync(ct);
        try
        {
            if (!refresh && IsFresh(_good)) return _good!;
            if (!refresh && DateTime.UtcNow < _nextTryUtc) return Fallback();

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "api/catalog");
                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"FileSync answered {(int)resp.StatusCode}.");
                var body = await resp.Content.ReadFromJsonAsync<CatalogBody>(Json, ct) ?? new CatalogBody();
                _good = new CatalogSnapshot(body.Plans ?? new(), body.Issues ?? new(), DateTime.UtcNow);
                _lastError = null;
                _nextTryUtc = DateTime.MinValue;
                return _good;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _lastError = "FileSync could not be reached: " + ex.Message;
                _nextTryUtc = DateTime.UtcNow + RetryAfterFailure;
                _log.LogWarning("FileSync catalog read failed: {Message}", ex.Message);
                return Fallback();
            }
        }
        finally { _lock.Release(); }
    }

    private bool IsFresh(CatalogSnapshot? s) => s != null && DateTime.UtcNow - s.FetchedUtc < _ttl;

    private CatalogSnapshot Fallback() => _good != null
        ? _good with { Stale = true, Error = _lastError }
        : throw new FileSyncUnavailableException(_lastError ?? "The file catalog is not available yet. Try again in a moment.");

    private sealed class CatalogBody
    {
        public List<CatalogPlanDto>? Plans { get; set; }
        public List<CatalogIssueDto>? Issues { get; set; }
    }
}
