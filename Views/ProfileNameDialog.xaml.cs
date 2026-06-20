using System.Windows;

namespace SevenDaysModLauncher.Views;

public partial class ProfileNameDialog : Window
{
    public ProfileNameDialog()
    {
        InitializeComponent();
    }

    public string ProfileName => ProfileNameTextBox.Text;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ProfileNameTextBox.Text))
        {
            DialogResult = true;
        }
    }
}