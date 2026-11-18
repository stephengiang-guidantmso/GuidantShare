using System.Net;
using System.Text;
using System.Text.Json;

namespace GuidantShare.Tests.Support;

/// <summary>
/// Fake ShareFile REST API at the HTTP level (token endpoint, Items, Children, Upload + ChunkUri).
/// The system tests run the REAL ShareFileService against this, so request building,
/// token refresh and the two-step upload are exercised end to end.
/// </summary>
public class FakeShareFileHttp : HttpMessageHandler
{
    public record Node(string Id, string Name, bool IsFolder, byte[]? Data = null);
    private readonly Dictionary<string, Node> _nodes = new();
    private readonly Dictionary<string, List<string>> _kids = new();
    public List<string> Requests { get; } = new();
    public int TokenCalls { get; private set; }
    public string ExpectedCode { get; set; } = "good-code";
    public byte[]? LastUploadBytes { get; private set; }
    public string? LastUploadName { get; private set; }

    public Node AddFolder(string id, string name, string? parent = null)
    {
        var n = new Node(id, name, true); _nodes[id] = n; _kids.TryAdd(id, new());
        if (parent != null) _kids[parent].Add(id);
        return n;
    }

    public void AddFile(string folderId, string name, byte[] data)
    {
        var id = "fi" + Guid.NewGuid().ToString("N")[..12];
        _nodes[id] = new Node(id, name, false, data); _kids[folderId].Add(id);
    }

    public IEnumerable<string> FileNames(string folderId) => _kids[folderId].Select(i => _nodes[i]).Where(n => !n.IsFolder).Select(n => n.Name);

    private static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private object Item(Node n) => new Dictionary<string, object?>
    {
        ["odata.type"] = n.IsFolder ? "ShareFile.Api.Models.Folder" : "ShareFile.Api.Models.File",
        ["Id"] = n.Id, ["Name"] = n.Name, ["FileSizeBytes"] = n.Data?.Length ?? 0,
        ["CreationDate"] = DateTime.UtcNow.ToString("O"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var url = req.RequestUri!;
        Requests.Add($"{req.Method} {url.AbsolutePath}");
        var path = Uri.UnescapeDataString(url.AbsolutePath);

        if (url.Host.EndsWith("sharefile.com") && path == "/oauth/token")
        {
            TokenCalls++;
            var form = await req.Content!.ReadAsStringAsync(ct);
            var f = System.Web.HttpUtility.ParseQueryString(form);
            if (f["grant_type"] == "authorization_code" && f["code"] != ExpectedCode)
                return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
            return Json(HttpStatusCode.OK, new { access_token = "access-" + TokenCalls, refresh_token = "refresh-token", expires_in = 28800 });
        }

        if (url.Host == "upload.test")           // the ChunkUri target
        {
            var id = path.Trim('/').Split('/').Last();
            LastUploadBytes = await req.Content!.ReadAsByteArrayAsync(ct);
            AddFile(id, LastUploadName!, LastUploadBytes);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") };
        }

        if (req.Headers.Authorization?.Parameter is null || !req.Headers.Authorization.Parameter.StartsWith("access-"))
            return Json(HttpStatusCode.Unauthorized, new { error = "unauthorized" });

        var m = System.Text.RegularExpressions.Regex.Match(path, @"/sf/v3/Items\(([^)]+)\)(/[A-Za-z]+)?$");
        if (!m.Success) return Json(HttpStatusCode.NotFound, new { error = "not found" });
        var itemId = m.Groups[1].Value; var tail = m.Groups[2].Value;
        if (!_nodes.TryGetValue(itemId, out var node)) return new HttpResponseMessage(HttpStatusCode.NotFound);

        if (tail == "" && req.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, Item(node));
        if (tail == "/Children" && req.Method == HttpMethod.Get)
            return Json(HttpStatusCode.OK, new { value = _kids[itemId].Select(i => Item(_nodes[i])).ToList() });
        if (tail == "/Upload" && req.Method == HttpMethod.Post)
        {
            LastUploadName = System.Web.HttpUtility.ParseQueryString(url.Query)["fileName"];
            return Json(HttpStatusCode.OK, new { ChunkUri = $"https://upload.test/chunk/{itemId}" });
        }
        return Json(HttpStatusCode.NotFound, new { error = "not found" });
    }
}
