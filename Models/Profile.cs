using System.Collections.Generic;

namespace SevenDaysModLauncher.Models;

public class Profile
{
    public string Name { get; set; } = string.Empty;
    public List<ModState> Mods { get; set; } = new();

    public class ModState
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>Стабильный ключ — имя папки мода. В старых профилях может отсутствовать, тогда ключ = Name.</summary>
        public string? FolderName { get; set; }
        public bool IsEnabled { get; set; }

        public string Key => string.IsNullOrWhiteSpace(FolderName) ? Name : FolderName;
    }
}