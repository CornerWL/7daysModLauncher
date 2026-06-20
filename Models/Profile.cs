using System.Collections.Generic;

namespace SevenDaysModLauncher.Models;

public class Profile
{
    public string Name { get; set; } = string.Empty;
    public List<ModState> Mods { get; set; } = new();

    public class ModState
    {
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }
}