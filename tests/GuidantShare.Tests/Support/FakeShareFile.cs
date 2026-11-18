using GuidantShare.Core;

namespace GuidantShare.Tests.Support;

/// <summary>In-memory stand-in for IShareFileService (used by the mocked integration tests).</summary>
public class FakeShareFile : IShareFileService
{
    private readonly Dictionary<string, SfItem> _items = new();
    private readonly Dictionary<string, List<SfItem>> _children = new();
    public bool Disconnected { get; set; }
    public bool FailUploads { get; set; }
    public Exception? ListError { get; set; }
    public List<(string FolderId, string Name, byte[] Bytes, bool Overwrite)> Uploads { get; } = new();
    public List<string> Lookups { get; } = new();

    public static string NewId() => "fo" + Guid.NewGuid().ToString("N")[..12];
    public static string LinkFor(string id) => $"https://test.sharefile.com/home/shared/{id}";

    public SfItem AddFolder(string? id = null, string name = "Folder", string? parentId = null)
    {
        var f = new SfItem(id ?? NewId(), name, 0, DateTime.UtcNow, true);
        _items[f.Id] = f; _children.TryAdd(f.Id, new());
        if (parentId != null) _children[parentId].Add(f);
        return f;
    }

    public SfItem AddFile(string folderId, string name, long size = 10, DateTime? modified = null, string? id = null)
    {
        var f = new SfItem(id ?? "fi" + Guid.NewGuid().ToString("N")[..12], name, size, modified ?? DateTime.UtcNow, false);
        _items[f.Id] = f; _children[folderId].Add(f);
        return f;
    }

    private void Guard() { if (Disconnected) throw new ShareFileNotConnectedException(); }

    public Task<ShareFileStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new ShareFileStatus(true, !Disconnected, "test", "admin@test.local", DateTime.UtcNow, null, null, "client"));
    public Task SaveAppSettingsAsync(string subdomain, string clientId, string? clientSecret, string? redirectUri, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> BuildAuthorizeUrlAsync(string state, CancellationToken ct = default) => Task.FromResult("https://test.sharefile.com/oauth/authorize?state=" + state);
    public Task ConnectWithCodeAsync(string code, string connectedBy, CancellationToken ct = default) { Disconnected = false; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken ct = default) { Disconnected = true; return Task.CompletedTask; }
    public Task<string?> ResolvePathAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<SfItem>> ListChildrenAsync(string folderId, CancellationToken ct = default)
    {
        Guard(); if (ListError != null) throw ListError;
        return Task.FromResult<IReadOnlyList<SfItem>>(_children.TryGetValue(folderId, out var l) ? l.ToList() : new List<SfItem>());
    }

    public Task<SfItem?> GetItemAsync(string id, CancellationToken ct = default)
    {
        Guard(); Lookups.Add(id);
        return Task.FromResult(_items.TryGetValue(id, out var i) ? i : null);
    }

    public async Task<SfItem> UploadAsync(string folderId, string fileName, Stream content, long length, bool overwrite, CancellationToken ct = default)
    {
        Guard();
        if (FailUploads) throw new HttpRequestException("ShareFile refused to start the upload (500).");
        using var ms = new MemoryStream(); await content.CopyToAsync(ms, ct);
        Uploads.Add((folderId, fileName, ms.ToArray(), overwrite));
        _children[folderId].RemoveAll(i => !i.IsFolder && i.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        return AddFile(folderId, fileName, length);
    }

    public Task<string> GetWebLinkAsync(string itemId, CancellationToken ct = default) => Task.FromResult(LinkFor(itemId));
}
