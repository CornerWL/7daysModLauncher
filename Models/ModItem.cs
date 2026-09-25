using CommunityToolkit.Mvvm.ComponentModel;

namespace SevenDaysModLauncher.Models;

public partial class ModItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _isEnabled;

    [ObservableProperty]
    private string? _version;

    [ObservableProperty]
    private string? _author;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _website;

    /// <summary>Стабильное имя папки мода (ключ для профилей). Name может быть DisplayName из ModInfo.</summary>
    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    /// <summary>Выделение в списке. Хранится в модели, чтобы переживать RefreshMods.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public bool IsDisabled => !IsEnabled;
}
