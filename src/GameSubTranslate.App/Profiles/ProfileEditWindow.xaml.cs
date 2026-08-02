using System.Windows;
using System.Windows.Controls;
using GameSubTranslate.App.Regions;
using GameSubTranslate.Config;
using GameSubTranslate.Profiles;

namespace GameSubTranslate.App.Profiles;

/// <summary>
/// Modal form to create or edit a GameProfile. Returns DialogResult=true on save.
/// Regions are managed here: "Add Region..." opens RegionSelectorWindow for drag-select.
/// </summary>
public partial class ProfileEditWindow : Window
{
    private readonly GameProfile? _existing;
    private readonly List<CaptureRegion> _regions;

    public GameProfile Result { get; private set; } = new();

    public ProfileEditWindow(GameProfile? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        Title = existing is null ? "New Profile" : $"Edit Profile - {existing.Name}";

        _regions = existing?.Regions.ToList() ?? new List<CaptureRegion>();
        RefreshRegionsList();

        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            ExecutableBox.Text = existing.ExecutableName ?? "";
            SelectCombo(SourceLangBox, existing.SourceLang);
            SelectCombo(TargetLangBox, existing.TargetLang);
            SelectOcrEngine(existing.OcrEngine);
            IntervalBox.Text = existing.CaptureIntervalMs.ToString();
        }
        else
        {
            SelectCombo(SourceLangBox, "auto");
            SelectCombo(TargetLangBox, "id");
            OcrEngineBox.SelectedIndex = 0;
            IntervalBox.Text = "800";
        }
    }

    private void RefreshRegionsList()
    {
        RegionsList.ItemsSource = null;
        RegionsList.ItemsSource = _regions;
    }

    private void AddRegion_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionSelectorWindow { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            // T7 single monitor: prompt name. T8 derives MonitorIndex from selection.
            var name = PromptRegionName(dlg.Result.Display);
            if (name is null) return;
            dlg.Result.RegionName = name;
            dlg.Result.SortOrder = _regions.Count;
            if (_regions.Count == 0) dlg.Result.IsActiveDefault = true;
            _regions.Add(dlg.Result);
            RefreshRegionsList();
        }
    }

    private void RemoveRegion_Click(object sender, RoutedEventArgs e)
    {
        if (RegionsList.SelectedItem is CaptureRegion r)
        {
            _regions.Remove(r);
            RefreshRegionsList();
        }
    }

    private string? PromptRegionName(string suggested)
    {
        // Lightweight inline dialog via InputBox replacement — minimal, no new window.
        var dlg = new RegionNameDialog($"Region {(_regions.Count + 1)}") { Owner = this };
        return dlg.ShowDialog() == true ? dlg.EnteredName : null;
    }

    private static void SelectCombo(System.Windows.Controls.ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.Text = value;
    }

    private void SelectOcrEngine(OcrEngineKind kind)
    {
        for (int i = 0; i < OcrEngineBox.Items.Count; i++)
        {
            if (OcrEngineBox.Items[i] is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag is string s
                && Enum.TryParse<OcrEngineKind>(s, out var parsed) && parsed == kind)
            {
                OcrEngineBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Name is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 100)
        {
            System.Windows.MessageBox.Show(this, "Capture interval must be a number at least 100 ms.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            IntervalBox.Focus();
            return;
        }

        var ocr = OcrEngineKind.Tesseract;
        if (OcrEngineBox.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag is string s)
            Enum.TryParse(s, out ocr);

        Result = new GameProfile
        {
            Id = _existing?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            ExecutableName = string.IsNullOrWhiteSpace(ExecutableBox.Text) ? null : ExecutableBox.Text.Trim(),
            SourceLang = (SourceLangBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "auto",
            TargetLang = (TargetLangBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "id",
            OcrEngine = ocr,
            CaptureIntervalMs = interval,
            Regions = _regions.ToList(),
            CreatedAt = _existing?.CreatedAt ?? DateTime.UtcNow,
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
