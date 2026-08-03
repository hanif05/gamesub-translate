using System.Windows;
using System.Windows.Controls;
using GameSubTranslate.Config;
// UseWindowsForms=true pulls in System.Windows.Forms as a global using → ambiguous MessageBox.
using MessageBox = System.Windows.MessageBox;

namespace GameSubTranslate.App.Settings;

/// <summary>T40: modal form to add/edit a fallback translation provider. Returns DialogResult=true on save.</summary>
public partial class ProviderEditWindow : Window
{
    private readonly ProviderConfig? _existing;

    /// <summary>The configured provider, read after a true dialog result.</summary>
    public ProviderConfig Result { get; private set; } = new();

    public ProviderEditWindow(ProviderConfig? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        Title = existing is null ? "Add Fallback Provider" : $"Edit Provider - {existing.Name}";

        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            BaseUrlBox.Text = existing.BaseUrl ?? "";
            ModelBox.Text = existing.Model ?? "";
            ApiKeyBox.Password = existing.ApiKey ?? "";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Name is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(BaseUrlBox.Text) || string.IsNullOrWhiteSpace(ModelBox.Text))
        {
            MessageBox.Show(this, "Base URL and Model are required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new ProviderConfig
        {
            Name = NameBox.Text.Trim(),
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = string.IsNullOrEmpty(ApiKeyBox.Password) ? _existing?.ApiKey : ApiKeyBox.Password,
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
