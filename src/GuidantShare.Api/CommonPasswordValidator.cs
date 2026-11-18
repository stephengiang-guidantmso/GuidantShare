using Microsoft.AspNetCore.Identity;
using GuidantShare.Data;

namespace GuidantShare.Api;

/// <summary>Rejects well-known/breached passwords (small built-in list; swap for a HIBP check later).</summary>
public class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password1234", "123456789012", "qwertyuiop12", "passwordpassword", "welcome12345", "letmein12345",
        "administrator", "iloveyou1234", "changeme1234", "password12345", "111111111111", "abcdefghijkl"
    };
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> m, AppUser user, string? password)
    {
        if (password != null && (Common.Contains(password) || password.Distinct().Count() < 5))
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Common", Description = "That password is too common. Choose a different one." }));
        if (password != null && user.Email != null && password.Contains(user.Email.Split('@')[0], StringComparison.OrdinalIgnoreCase) && user.Email.Split('@')[0].Length > 3)
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Email", Description = "Password must not contain your email name." }));
        return Task.FromResult(IdentityResult.Success);
    }
}
