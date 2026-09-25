using SevenDaysModLauncher.Services;

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
