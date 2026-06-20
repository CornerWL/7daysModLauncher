using System.Windows;
using System.Windows.Controls;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using SevenDaysModLauncher.ViewModels;

namespace SevenDaysModLauncher.Views;

public partial class MainWindow : Window
{
    private bool _isLoadingProfile = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (DataContext is MainViewModel vm)
            {
                vm.InstallModFromDropCommand.Execute(files);
            }
        }
    }

    private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingProfile) return;
        
        if (DataContext is MainViewModel vm && e.AddedItems?.Count > 0)
        {
            _isLoadingProfile = true;
            vm.LoadProfileCommand.Execute(null);
            _isLoadingProfile = false;
        }
    }
}
