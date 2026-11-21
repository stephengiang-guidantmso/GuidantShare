using System.Collections.Concurrent;
using GuidantShare.Core;

namespace GuidantShare.ShareFile;

/// <summary>
/// Offline stand-in for ShareFile for local development (<c>ShareFile:Mode=Mock</c>). Never use it in production.
/// Every folder id the catalog mentions "exists": it is created on first use with a few sample files and one subfolder,
/// and uploads are kept in memory only.
/// </summary>
public class MockShareFileService : IShareFileService
{
    private readonly ConcurrentDictionary<string, SfItem> _items = new();
    private readonly ConcurrentDictionary<string, List<SfItem>> _children = new();
    private bool _connected = true;

    public Task<ShareFileStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new ShareFileStatus(Configured: true, Connected: _connected, Subdomain: "mock", ConnectedBy: "mock mode",
            ConnectedUtc: DateTime.UtcNow, Error: null, RedirectUri: null, ClientId: "mock"));

    public Task SaveAppSettingsAsync(string subdomain, string clientId, string? clientSecret, string? redirectUri, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> BuildAuthorizeUrlAsync(string state, CancellationToken ct = default) => Task.FromResult("about:blank#mock-sharefile-" + state);
    public Task ConnectWithCodeAsync(string code, string connectedBy, CancellationToken ct = default) { _connected = true; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken ct = default) { _connected = false; return Task.CompletedTask; }
    public Task<string?> ResolvePathAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);

    private void Guard() { if (!_connected) throw new ShareFileNotConnectedException(); }

    private List<SfItem> Folder(string id)
    {
        return _children.GetOrAdd(id, key =>
        {
            _items[key] = new SfItem(key, "Mock folder", 0, DateTime.UtcNow, true);
            var list = new List<SfItem>();
            var sub = new SfItem(key + "-archive", "Archive", 0, DateTime.UtcNow, true);
            _items[sub.Id] = sub;
            _children[sub.Id] = new List<SfItem> { new SfItem(key + "-old1", "old-report.csv", 2048, DateTime.UtcNow.AddDays(-30), false) };
            list.Add(sub);
            list.Add(new SfItem(key + "-f1", "sample-1.csv", 1024, DateTime.UtcNow.AddHours(-3), false));
            list.Add(new SfItem(key + "-f2", "sample-2.txt", 512, DateTime.UtcNow.AddDays(-1), false));
            return list;
        });
    }

    public Task<IReadOnlyList<SfItem>> ListChildrenAsync(string folderId, CancellationToken ct = default)
    {
        Guard();
        var list = Folder(folderId);
        lock (list) return Task.FromResult<IReadOnlyList<SfItem>>(list.ToList());
    }

    public Task<SfItem?> GetItemAsync(string id, CancellationToken ct = default)
    {
        Guard();
        if (id.StartsWith("fo", StringComparison.Ordinal)) Folder(id);
        return Task.FromResult(_items.TryGetValue(id, out var i) ? i : null);
    }

    public async Task<SfItem> UploadAsync(string folderId, string fileName, Stream content, long length, bool overwrite, CancellationToken ct = default)
    {
        Guard();
        var list = Folder(folderId);
        await content.CopyToAsync(Stream.Null, ct); // the bytes are discarded; only the listing entry is kept
        var item = new SfItem("fi" + Guid.NewGuid().ToString("N")[..12], fileName, length, DateTime.UtcNow, false);
        lock (list)
        {
            list.RemoveAll(i => !i.IsFolder && i.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            list.Add(item);
        }
        _items[item.Id] = item;
        return item;
    }

    public Task<string> GetWebLinkAsync(string itemId, CancellationToken ct = default) =>
        Task.FromResult($"https://mock.sharefile.example/home/shared/{itemId}");
}
