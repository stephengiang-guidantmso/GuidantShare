using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GuidantShare.Core;

namespace GuidantShare.Data;

public class SecretStore : ISecretStore
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AesGcmCipher _cipher;
    public SecretStore(IServiceScopeFactory scopes, AesGcmCipher cipher) { _scopes = scopes; _cipher = cipher; }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        using var s = _scopes.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSecrets.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        return row == null ? null : _cipher.Decrypt(row.Value);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        using var s = _scopes.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSecrets.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row == null) db.AppSecrets.Add(row = new AppSecret { Key = key });
        row.Value = _cipher.Encrypt(value);
        row.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var s = _scopes.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AppSecrets.Where(x => x.Key == key).ExecuteDeleteAsync(ct);
    }
}
