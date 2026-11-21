using GuidantShare.Core;

namespace GuidantShare.Tests.Support;

/// <summary>Direction of a folder in the fake catalog (the real catalog uses the strings "Download" and "Upload").</summary>
public enum Direction { Download, Upload }

/// <summary>In-memory stand-in for FileSync: a catalog the test builds, plus a switch to simulate FileSync being down.</summary>
public class FakeFileSync : IFileSyncClient
{
    public List<CatalogPlanDto> Plans { get; } = new();
    public List<CatalogIssueDto> Issues { get; } = new();
    public bool Down { get; set; }
    public int CatalogReads { get; private set; }
    public List<(string Method, string Path)> Calls { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage>? Handler { get; set; }
    private CatalogSnapshot? _last;

    public static string Slug(string s) => string.Concat(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));

    public CatalogFolderDto AddFolder(string plan, string label, Direction direction, string shareFileFolderId, string? displayPath = null)
    {
        var p = Plans.FirstOrDefault(x => x.Name == plan);
        if (p == null) { p = new CatalogPlanDto(Slug(plan), plan, new()); Plans.Add(p); }
        var dir = direction.ToString();
        var f = new CatalogFolderDto($"{dir.ToLowerInvariant()}-{Slug(label)}", label, dir, shareFileFolderId, displayPath ?? $"Shared Folders/{plan}/{label}", new() { "wf-" + Slug(plan + label) });
        p.Folders.Add(f);
        return f;
    }

    public Task<CatalogSnapshot> GetCatalogAsync(bool refresh = false, CancellationToken ct = default)
    {
        CatalogReads++;
        if (Down)
        {
            if (_last != null) return Task.FromResult(_last with { Stale = true, Error = "FileSync could not be reached." });
            throw new FileSyncUnavailableException("FileSync could not be reached.");
        }
        _last = new CatalogSnapshot(Plans.ToList(), Issues.ToList(), DateTime.UtcNow);
        return Task.FromResult(_last);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        Calls.Add((request.Method.Method, request.RequestUri!.ToString().TrimStart('/')));
        if (Down) throw new HttpRequestException("connection refused");
        var resp = Handler?.Invoke(request) ?? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(resp);
    }
}
