namespace GuidantShare.Core;

// The health plans and folders come from FileSync (GET /api/catalog). GuidantShare keeps no catalog of its own.

/// <summary>One folder: all FileSync jobs with the same plan, direction and folder label, merged.</summary>
public record CatalogFolderDto(string Key, string Label, string Direction, string ShareFileFolderId, string ShareFileDisplayPath, List<string> WorkflowIds);

public record CatalogPlanDto(string Key, string Name, List<CatalogFolderDto> Folders);

public record CatalogIssueDto(string Plan, string Folder, string Direction, string Message, List<string> WorkflowIds);

/// <summary>The catalog as last read from FileSync. <see cref="Stale"/> is true when FileSync could not be reached and this is the last good copy.</summary>
public record CatalogSnapshot(List<CatalogPlanDto> Plans, List<CatalogIssueDto> Issues, DateTime FetchedUtc, bool Stale = false, string? Error = null);

public class FileSyncUnavailableException : Exception
{
    public FileSyncUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IFileSyncClient
{
    /// <summary>The catalog, cached for a short time. <paramref name="refresh"/> skips the cache. Throws <see cref="FileSyncUnavailableException"/> when FileSync cannot be reached and nothing was ever loaded.</summary>
    Task<CatalogSnapshot> GetCatalogAsync(bool refresh = false, CancellationToken ct = default);

    /// <summary>Sends one request to FileSync's API (the base address and API key are added). The caller disposes the response.</summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default);
}
