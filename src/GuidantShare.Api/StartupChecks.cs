using GuidantShare.Api.Services;
using GuidantShare.Core;
using GuidantShare.Data;
using Microsoft.EntityFrameworkCore;

namespace GuidantShare.Api;

public static class StartupChecks
{
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Logger;
        var email = app.Services.GetRequiredService<IEmailSender>();
        if (email is GraphEmailSender g) { var err = await g.CheckAsync(); if (err != null) log.LogError("Email misconfigured: {Err}", err); }
        try
        {
            var sf = app.Services.GetRequiredService<IShareFileService>();
            var st = await sf.GetStatusAsync();
            if (!st.Connected) { log.LogWarning("ShareFile is not connected. A SuperAdmin must connect it in the app."); return; }
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var e in await db.CatalogEntries.Where(e => e.SharefileFolderId != null).ToListAsync())
                if (await sf.GetItemAsync(e.SharefileFolderId!) == null)
                    log.LogError("Catalog entry {Id} folder {Path} no longer exists in ShareFile", e.EntryId, e.SharefileDisplayPath);
        }
        catch (Exception ex) { log.LogError("ShareFile startup check failed: {Msg}", ex.Message); }
    }
}
