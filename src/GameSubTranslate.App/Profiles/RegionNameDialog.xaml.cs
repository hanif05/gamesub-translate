using System.Windows;

namespace GameSubTranslate.App.Profiles;

public partial class RegionNameDialog : Window
{
    public string EnteredName { get; private set; } = "";

    public RegionNameDialog(string initial)
    {
        InitializeComponent();
        NameInput.Text = initial;
        Loaded += (_, _) => { NameInput.SelectAll(); NameInput.Focus(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        EnteredName = string.IsNullOrWhiteSpace(NameInput.Text) ? "Region" : NameInput.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
