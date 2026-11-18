using System.Diagnostics;

namespace GuidantShare.Tests.SystemTests;

/// <summary>scripts/system.sh is only checked for its safe paths (help and bad input); it never starts or stops anything here.</summary>
[Trait("Category", "System")]
public class ScriptSystemTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "GuidantShare.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static (int code, string output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Path.Combine(Root(), "scripts", "system.sh")); foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(); return (p.ExitCode, text);
    }

    [Fact]
    public void Help_lists_every_option_and_succeeds()
    {
        var (code, text) = Run("--help");
        Assert.Equal(0, code);
        foreach (var opt in new[] { "--start", "--stop", "--restart", "--help" }) Assert.Contains(opt, text);
    }

    [Theory]
    [InlineData]
    [InlineData("--bogus")]
    public void No_option_or_an_unknown_one_prints_usage_and_fails(params string[] args)
    {
        var (code, text) = Run(args);
        Assert.NotEqual(0, code); Assert.Contains("Usage", text);
    }

    [Fact]
    public void The_script_is_syntactically_valid_bash()
    {
        var psi = new ProcessStartInfo("bash") { RedirectStandardError = true };
        psi.ArgumentList.Add("-n"); psi.ArgumentList.Add(Path.Combine(Root(), "scripts", "system.sh"));
        using var p = Process.Start(psi)!; p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
