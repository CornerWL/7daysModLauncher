using SevenDaysModLauncher.Services;
using SevenDaysModLauncher.Models;

namespace SevenDaysModLauncher.Tests;

public class GamePathHelperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("C:\\Games\\7DTD", "C:\\Games\\7DTD")]
    [InlineData("C:\\Games\\7DTD\\Mods", "C:\\Games\\7DTD")]
    [InlineData("C:\\Games\\7DTD\\Mods_Disabled", "C:\\Games\\7DTD")]
    [InlineData("C:\\Games\\7DTD\\Mods\\", "C:\\Games\\7DTD")]
    public void NormalizeGameFolderPath_StripsModSubfolders(string? input, string expected)
    {
        Assert.Equal(expected, GamePathHelper.NormalizeGameFolderPath(input));
    }

    [Fact]
    public void IsValidGameFolder_ReturnsFalse_ForMissingDir()
    {
        Assert.False(GamePathHelper.IsValidGameFolder("Z:\\definitely\\not\\exists"));
    }

    [Fact]
    public void IsValidGameFolder_ReturnsFalse_WhenExeMissing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.False(GamePathHelper.IsValidGameFolder(tmp));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void IsValidGameFolder_ReturnsTrue_WhenExeExists()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "7DaysToDie.exe"), "fake");
            Assert.True(GamePathHelper.IsValidGameFolder(tmp));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void EnsureModDirectories_CreatesBoth()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            GamePathHelper.EnsureModDirectories(tmp);
            Assert.True(Directory.Exists(Path.Combine(tmp, "Mods")));
            Assert.True(Directory.Exists(Path.Combine(tmp, "Mods_Disabled")));
        }
        finally
        {
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, true);
        }
    }
}

public class GameLauncherServiceTests
{
    [Fact]
    public void FindExe_ReturnsNull_ForMissingDir()
    {
        var svc = new GameLauncherService();
        Assert.Null(svc.FindExe("Z:\\definitely\\not\\exists"));
    }

    [Fact]
    public void TryLaunch_ReturnsFalse_WhenExeMissing()
    {
        var svc = new GameLauncherService();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.False(svc.TryLaunch(tmp, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}

public class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.0", 1)]
    [InlineData("1.1.0", "1.1.0", 0)]
    [InlineData("v1.0.9", "1.1.0", -1)]
    [InlineData("v2.0", "1.9.9", 1)]
    [InlineData("v1.1.0-beta", "1.1.0", 0)]
    public void CompareVersions_Works(string tag, string current, int expected)
    {
        Assert.Equal(expected, Math.Sign(UpdateCheckService.CompareVersions(tag, current)));
    }
}

public class ApplyProfileTests : IDisposable
{
    private readonly string _gameDir;
    private readonly ModService _svc = new();

    public ApplyProfileTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "7dtd_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameDir, "Mods", "0_TFP_Harmony"));
        File.WriteAllText(
            Path.Combine(_gameDir, "Mods", "0_TFP_Harmony", "ModInfo.xml"),
            "<ModInfo Name=\"Harmony Display\" Author=\"A\" Version=\"1.0\" />");
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDir))
            Directory.Delete(_gameDir, true);
    }

    [Fact]
    public void ApplyProfile_MatchesLegacyProfile_ByDisplayName_AndHeals()
    {
        // ������ �������: FolderName ���, Name = ������������ ���
        var profile = new Profile
        {
            Name = "p1",
            Mods = new List<Profile.ModState>
            {
                new() { Name = "Harmony Display", IsEnabled = false },
            }
        };

        var result = _svc.ApplyProfile(_gameDir, profile);

        Assert.Empty(result.Missing);
        Assert.Equal(1, result.Moved);
        Assert.True(result.Healed);
        Assert.True(Directory.Exists(Path.Combine(_gameDir, "Mods_Disabled", "0_TFP_Harmony")));
        Assert.Equal("0_TFP_Harmony", profile.Mods[0].FolderName);
    }

    [Fact]
    public void ApplyProfile_MovesBack_WhenEnabled()
    {
        Directory.CreateDirectory(Path.Combine(_gameDir, "Mods_Disabled"));
        Directory.Move(
            Path.Combine(_gameDir, "Mods", "0_TFP_Harmony"),
            Path.Combine(_gameDir, "Mods_Disabled", "0_TFP_Harmony"));
        var profile = new Profile
        {
            Name = "p2",
            Mods = new List<Profile.ModState>
            {
                new() { Name = "Harmony Display", FolderName = "0_TFP_Harmony", IsEnabled = true },
            }
        };

        var result = _svc.ApplyProfile(_gameDir, profile);

        Assert.Empty(result.Missing);
        Assert.Equal(1, result.Moved);
        Assert.True(Directory.Exists(Path.Combine(_gameDir, "Mods", "0_TFP_Harmony")));
    }
}

public class NexusParseTests
{
    [Theory]
    [InlineData("10784", 10784)]
    [InlineData("  10784  ", 10784)]
    [InlineData("https://www.nexusmods.com/7daystodie/mods/10784", 10784)]
    [InlineData("https://www.nexusmods.com/7daystodie/mods/10784?tab=files", 10784)]
    [InlineData("nxm://7daystodie/mods/10784/files/1", 10784)]
    [InlineData("not a link", null)]
    [InlineData("", null)]
    public void ParseModId_Works(string input, int? expected)
    {
        Assert.Equal(expected, NexusApiService.ParseModId(input));
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/7daystodie/mods/10784", "7daystodie")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604", "skyrimspecialedition")]
    [InlineData("10784", null)]
    public void ParseGameDomain_Works(string input, string? expected)
    {
        Assert.Equal(expected, NexusApiService.ParseGameDomain(input));
    }
}

public class NexusOAuthTests
{
    [Fact]
    public void PkceChallenge_MatchesRfc7636Vector()
    {
        // �������� ������ �� RFC 7636, Appendix B
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            NexusOAuthService.CreateCodeChallenge(verifier));
    }

    [Fact]
    public void PkceVerifier_HasValidLength()
    {
        var v = NexusOAuthService.CreateCodeVerifier();
        Assert.InRange(v.Length, 43, 128);
        Assert.DoesNotContain("=", v);
        Assert.DoesNotContain("+", v);
        Assert.DoesNotContain("/", v);
    }

    [Fact]
    public void BuildAuthorizeUrl_RequiresClientId()
    {
        var cfg = new NexusOAuthService.OAuthConfig("https://x/auth", "https://x/token", "", "");
        Assert.Throws<InvalidOperationException>(() =>
            NexusOAuthService.BuildAuthorizeUrl(cfg, "http://127.0.0.1:1/callback/", "s", "c"));
    }

    [Fact]
    public void BuildAuthorizeUrl_ContainsPkceParams()
    {
        var cfg = new NexusOAuthService.OAuthConfig("https://x/auth", "https://x/token", "myapp", "");
        var url = NexusOAuthService.BuildAuthorizeUrl(cfg, "http://127.0.0.1:9/callback/", "state1", "chall1");
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=myapp", url);
        Assert.Contains("code_challenge=chall1", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=state1", url);
    }
}
