namespace SevenDaysModLauncher.Models;

public class AppSettings
{
    public string? GameFolderPath { get; set; }
    public string? SelectedProfile { get; set; }
    /// <summary>Личный API-ключ Nexus (ручной ввод, фолбэк если нет OAuth).</summary>
    public string? NexusApiKey { get; set; }
    /// <summary>OAuth-токены Nexus в JSON (access/refresh/expires).</summary>
    public string? NexusOAuth { get; set; }
}
