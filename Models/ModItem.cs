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
    private string _folderPath = string.Empty;

    public bool IsDisabled => !IsEnabled;
}
