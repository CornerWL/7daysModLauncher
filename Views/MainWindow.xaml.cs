using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using WpfListViewItem = System.Windows.Controls.ListViewItem;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfSelector = System.Windows.Controls.Primitives.Selector;
using SevenDaysModLauncher.ViewModels;

namespace SevenDaysModLauncher.Views;

public partial class MainWindow : Window
{
    private bool _isLoadingProfile = false;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    /// <summary>Темный титл-бар под тему (иначе белая полоса Windows поверх темного UI).</summary>
    private void EnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            // 20 = Win11, 19 = Win10 1903+
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch
        {
            // Не поддерживается — остаемся со светлым титл-баром
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

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

    /// <summary>Клик по пустому месту списка (не по карточке и не по скроллбару) — снять выделение.</summary>
    private void ModsList_ClickEmptyDeselect(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current is WpfListViewItem || current is WpfScrollBar)
                return;
            if (current is WpfSelector)
                break;
            current = VisualTreeHelper.GetParent(current);
        }

        if (DataContext is MainViewModel vm)
            vm.ClearSelectionCommand.Execute(null);
    }
}
