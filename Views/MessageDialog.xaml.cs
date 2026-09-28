using System.Windows;

namespace SevenDaysModLauncher.Views;

/// <summary>Свой темный диалог вместо системного MessageBox.</summary>
public partial class MessageDialog : Window
{
    private MessageDialog(string title, string message, string primary, string? secondary, bool isError)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        };
        if (secondary == null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondary;
        }
        if (isError)
            AccentDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "DangerBrush");
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static Window? OwnerWindow => System.Windows.Application.Current?.MainWindow;

    public static void Notify(string title, string message, bool isError = false)
    {
        var dlg = new MessageDialog(title, message, "OK", null, isError) { Owner = OwnerWindow };
        dlg.ShowDialog();
    }

    public static bool Confirm(string title, string message)
    {
        var dlg = new MessageDialog(title, message, "Yes", "No", false) { Owner = OwnerWindow };
        return dlg.ShowDialog() == true;
    }
}
