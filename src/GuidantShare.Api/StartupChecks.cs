using GuidantShare.Api.Services;
using GuidantShare.Core;

namespace GuidantShare.Api;

public static class StartupChecks
{
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Logger;
        var email = app.Services.GetRequiredService<IEmailSender>();
        if (email is GraphEmailSender g) { var err = await g.CheckAsync(); if (err != null) log.LogError("Email misconfigured: {Err}", err); }

        var fsOpts = app.Services.GetRequiredService<FileSyncOptions>();
        if (string.IsNullOrWhiteSpace(fsOpts.ApiKey) && !app.Environment.IsDevelopment())
            log.LogError("FileSync:ApiKey is not set. FileSync will refuse every call, so no health plans will show.");

        try
        {
            var sf = app.Services.GetRequiredService<IShareFileService>();
            var st = await sf.GetStatusAsync();
            if (!st.Connected) { log.LogWarning("ShareFile is not connected. A SuperAdmin must connect it in the app."); return; }

            var fs = app.Services.GetRequiredService<IFileSyncClient>();
            var catalog = await fs.GetCatalogAsync();
            foreach (var plan in catalog.Plans)
                foreach (var f in plan.Folders)
                    if (await sf.GetItemAsync(f.ShareFileFolderId) == null)
                        log.LogError("{Plan} / {Folder} ({Direction}): ShareFile folder {Path} can't be opened with GuidantShare's ShareFile connection", plan.Name, f.Label, f.Direction, f.ShareFileDisplayPath);
        }
        catch (FileSyncUnavailableException ex) { log.LogWarning("FileSync catalog not available at startup: {Msg}", ex.Message); }
        catch (Exception ex) { log.LogError("ShareFile startup check failed: {Msg}", ex.Message); }
    }
}
