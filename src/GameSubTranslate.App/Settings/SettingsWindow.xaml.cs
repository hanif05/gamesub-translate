using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.App.Profiles;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;
using GameSubTranslate.Translation;
// UseWindowsForms=true pulls in System.Windows.Forms as a global using → these collide with WPF types.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;

namespace GameSubTranslate.App.Settings;

/// <summary>
/// T23 settings panel. Edits a private copy of AppSettings; Save writes it via SettingsStore.
/// Cancel discards. Hotkey "Change" captures the next keypress on this window.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly OverlayWindow? _overlay;
    private readonly ProfileRepository _repo;
    private AppSettings _settings; // working copy — MainWindow's instance stays untouched until Save
    private readonly Database _db;
    private bool _capturingHotkey;
    private string _hotkeySetting = "";
    private TextBox? _colorTarget;

    /// <summary>True once the user saved. Read after Close to decide whether settings need reloading.</summary>
    public bool Saved { get; private set; }

    private static readonly string[] Palette =
    {
        "#FFFFFF", "#CCCCCC", "#808080", "#000000",
        "#FFFF00", "#00FF00", "#00FFFF", "#FF00FF",
        "#FF0000", "#FF8800", "#8888FF", "#88FF88",
        "#CC000000", "#AA000000", "#80000000",
        "#66FFFFFF", "#AAFFFFFF", "#CCFFFFFF",
    };

    public SettingsWindow(OverlayWindow? overlay = null)
    {
        InitializeComponent();
        _overlay = overlay;
        _settings = new SettingsStore().Load();
        _db = new Database();
        _db.EnsureSchema();
        _repo = new ProfileRepository(_db);
        LoadSettings();
        RefreshProfiles();
        BuildPalette();

        FontSizeSlider.ValueChanged += (_, _) =>
        {
            FontSizeText.Text = FontSizeSlider.Value.ToString("0");
            UpdatePreview();
        };
        OpacitySlider.ValueChanged += (_, _) =>
        {
            OpacityText.Text = OpacitySlider.Value.ToString("0.00");
            UpdatePreview();
        };
        UpdatePreview();

        // T48: live validation on interval change.
        IntervalBox.TextChanged += (_, _) => ValidateInterval();

        // T44: About tab reads version from version.txt next to the exe (shipped by csproj).
        // Fall back to a single dash on dev runs that pre-date T44.
        var versionFile = Path.Combine(AppContext.BaseDirectory, "version.txt");
        var version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "-";
        AboutVersion.Text = $"Version {version}";
    }

    private void LoadSettings()
    {
        BaseUrlBox.Text = _settings.BaseUrl ?? "";
        ModelBox.Text = _settings.Model ?? "";
        SelectCombo(SourceLangBox, _settings.SourceLang);
        SelectCombo(TargetLangBox, _settings.TargetLang);
        IntervalBox.Text = _settings.CaptureIntervalMs.ToString();
        SelectOcrEngine(_settings.OcrEngine);
        FontFamilyBox.Text = _settings.OverlayFontFamily;
        FontSizeSlider.Value = _settings.OverlayFontSize;
        TextColorBox.Text = _settings.OverlayTextColor;
        BgColorBox.Text = _settings.OverlayBgColor;
        OpacitySlider.Value = _settings.OverlayOpacity;
        ToggleHotkeyText.Text = _settings.HotkeyToggleOverlay;
        PauseHotkeyText.Text = _settings.HotkeyPauseCapture;
        SettingsHotkeyText.Text = _settings.HotkeyOpenSettings;
        ManualHotkeyText.Text = _settings.HotkeyManualCapture;
        RefreshProviders();
    }

    // ---- T40 fallback providers ----

    private void RefreshProviders() => ProviderList.ItemsSource = _settings.Providers.ToList();

    private void ProviderAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProviderEditWindow();
        if (dlg.ShowDialog() == true)
        {
            _settings.Providers.Add(dlg.Result);
            RefreshProviders();
        }
    }

    private void ProviderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { } sel) return;
        var dlg = new ProviderEditWindow(sel);
        if (dlg.ShowDialog() == true)
        {
            var i = _settings.Providers.IndexOf(sel);
            _settings.Providers[i] = dlg.Result;
            RefreshProviders();
        }
    }

    private void ProviderRemove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { } sel) return;
        _settings.Providers.Remove(sel);
        RefreshProviders();
    }

    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ItemsSource snapshot shares object refs, so SelectedItem drives SelectedProvider directly.
    }

    private ProviderConfig? SelectedProvider => ProviderList.SelectedItem as ProviderConfig;

    // T48: reorder fallback providers. The list order IS the failover order (T40).
    private void ProviderUp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { } sel) return;
        var i = _settings.Providers.IndexOf(sel);
        if (i > 0)
        {
            _settings.Providers.RemoveAt(i);
            _settings.Providers.Insert(i - 1, sel);
            RefreshProviders();
            ProviderList.SelectedItem = sel;
        }
    }

    private void ProviderDown_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { } sel) return;
        var i = _settings.Providers.IndexOf(sel);
        if (i >= 0 && i < _settings.Providers.Count - 1)
        {
            _settings.Providers.RemoveAt(i);
            _settings.Providers.Insert(i + 1, sel);
            RefreshProviders();
            ProviderList.SelectedItem = sel;
        }
    }

    // ---- About / Logs ----

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GameSubTranslate", "logs");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = dir,
            UseShellExecute = true,
        });
    }

    /// <summary>T48: factory-reset every editable field (preserves API key — typing it back is annoying).</summary>
    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var ok = MessageBox.Show(this,
            "Reset all settings to factory defaults? Your API key will be preserved.",
            "Reset to Defaults",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.OK) return;
        var apiKey = _settings.ApiKey;
        _settings = new AppSettings { ApiKey = apiKey };
        LoadSettings();
        UpdatePreview();
        ValidateInterval();
    }

    // ---- T48: Overlay live preview + validation ----

    /// <summary>Pushes current Overlay tab values into the preview Border — no Save needed.</summary>
    private void UpdatePreview()
    {
        PreviewText.FontFamily = FontFor(FontFamilyBox.Text);
        var size = FontSizeSlider.Value;
        PreviewText.FontSize = size;
        PreviewText.Foreground = BrushFor(string.IsNullOrWhiteSpace(TextColorBox.Text) ? "#FFFFFF" : TextColorBox.Text.Trim());
        PreviewCard.Background = BrushFor(string.IsNullOrWhiteSpace(BgColorBox.Text) ? "#CC000000" : BgColorBox.Text.Trim());
        // Opacity is multiplicative — apply on the inner element so the border stays opaque-ish.
        PreviewText.Opacity = OpacitySlider.Value;
    }

    private void OverlayStyle_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void IntervalBox_Changed(object sender, TextChangedEventArgs e) => ValidateInterval();

    private void ValidateInterval()
    {
        if (int.TryParse(IntervalBox.Text, out var n) && n >= 100)
        {
            IntervalWarn.Text = "";
        }
        else
        {
            IntervalWarn.Text = "Must be a number ≥ 100 ms";
        }
    }

    private static FontFamily FontFor(string name)
    {
        try { return string.IsNullOrWhiteSpace(name) ? new FontFamily("Segoe UI") : new FontFamily(name); }
        catch { return new FontFamily("Segoe UI"); }
    }

    // ---- API & Model ----

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var key = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? _settings.ApiKey : ApiKeyBox.Password;
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? _settings.BaseUrl : BaseUrlBox.Text.Trim();
        var model = string.IsNullOrWhiteSpace(ModelBox.Text) ? _settings.Model : ModelBox.Text.Trim();

        TestBtn.IsEnabled = false;
        TestResult.Text = "Testing…";
        try
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            {
                TestResult.Text = "Fill Base URL and Model first.";
                return;
            }
            var client = new TranslationClient(key ?? "", baseUrl, model, "auto", _settings.TargetLang);
            var result = await client.TestConnectionAsync();
            TestResult.Text = result is null ? "Connected." : $"Connected — sample: \"{result}\"";
        }
        catch (TranslationException ex) { TestResult.Text = $"Failed: {ex.Message}"; }
        catch (Exception ex) { TestResult.Text = $"Failed: {ex.Message}"; }
        finally { TestBtn.IsEnabled = true; }
    }

    // ---- Overlay ----

    private void PickPosition_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay is null)
        {
            MessageBox.Show(this, "Overlay not available.", "Pick Position", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, "Drag the overlay to the desired position. Release the mouse button to save.",
            "Pick Position", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        _overlay.BeginReposition((x, y) =>
        {
            _settings.OverlayX = x;
            _settings.OverlayY = y;
        });
    }

    private void FontSizePlus_Click(object sender, RoutedEventArgs e)
        => FontSizeSlider.Value = System.Math.Min(FontSizeSlider.Maximum, FontSizeSlider.Value + 1);

    private void FontSizeMinus_Click(object sender, RoutedEventArgs e)
        => FontSizeSlider.Value = System.Math.Max(FontSizeSlider.Minimum, FontSizeSlider.Value - 1);

    private void TextColor_Click(object sender, RoutedEventArgs e)
        => ShowColorPalette(TextColorBox, (Button)sender);

    private void BgColor_Click(object sender, RoutedEventArgs e)
        => ShowColorPalette(BgColorBox, (Button)sender);

    private void ShowColorPalette(TextBox target, Button anchor)
    {
        _colorTarget = target;
        ColorPopup.PlacementTarget = anchor;
        ColorPopup.IsOpen = true;
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string hex } && _colorTarget is not null)
        {
            _colorTarget.Text = hex;
            ColorPopup.IsOpen = false;
        }
    }

    private void BuildPalette()
    {
        foreach (var hex in Palette)
        {
            var swatch = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(2),
                Background = BrushFor(hex),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Tag = hex,
            };
            swatch.MouseLeftButtonDown += Swatch_Click;
            ColorPalette.Children.Add(swatch);
        }
    }

    private static Brush BrushFor(string hex)
    {
        try
        {
            var sc = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            sc.Freeze();
            return sc;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    // ---- Hotkeys ----

    private void ToggleHotkey_Change(object sender, RoutedEventArgs e)
        => BeginHotkeyCapture(nameof(AppSettings.HotkeyToggleOverlay));
    private void PauseHotkey_Change(object sender, RoutedEventArgs e)
        => BeginHotkeyCapture(nameof(AppSettings.HotkeyPauseCapture));
    private void SettingsHotkey_Change(object sender, RoutedEventArgs e)
        => BeginHotkeyCapture(nameof(AppSettings.HotkeyOpenSettings));
    private void ManualHotkey_Change(object sender, RoutedEventArgs e)
        => BeginHotkeyCapture(nameof(AppSettings.HotkeyManualCapture));

    private void BeginHotkeyCapture(string settingName)
    {
        _capturingHotkey = true;
        _hotkeySetting = settingName;
        HotkeyHint.Text = "Press the new keys… (ESC to cancel)";
        HotkeyHint.Visibility = Visibility.Visible;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturingHotkey)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return; // wait for the actual key
            if (key == Key.Escape)
            {
                _capturingHotkey = false;
                HotkeyHint.Visibility = Visibility.Collapsed;
                return;
            }
            var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
            if (mods == ModifierKeys.None) return; // require a modifier — plain keys are too dangerous globally
            var spec = GlobalHotkeyManager.Format(mods, key);
            if (IsHotkeyFree(spec, _hotkeySetting))
            {
                AssignHotkey(_hotkeySetting, spec);
                _capturingHotkey = false;
                HotkeyHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                MessageBox.Show(this, $"Hotkey {spec} is already bound to another action.",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private bool IsHotkeyFree(string spec, string settingName)
    {
        foreach (var (value, name) in new[]
        {
            (_settings.HotkeyToggleOverlay, nameof(AppSettings.HotkeyToggleOverlay)),
            (_settings.HotkeyPauseCapture, nameof(AppSettings.HotkeyPauseCapture)),
            (_settings.HotkeyOpenSettings, nameof(AppSettings.HotkeyOpenSettings)),
            (_settings.HotkeyManualCapture, nameof(AppSettings.HotkeyManualCapture)),
        })
        {
            if (name == settingName) continue;
            if (string.Equals(value, spec, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private void AssignHotkey(string settingName, string spec)
    {
        switch (settingName)
        {
            case nameof(AppSettings.HotkeyToggleOverlay): _settings.HotkeyToggleOverlay = spec; ToggleHotkeyText.Text = spec; break;
            case nameof(AppSettings.HotkeyPauseCapture): _settings.HotkeyPauseCapture = spec; PauseHotkeyText.Text = spec; break;
            case nameof(AppSettings.HotkeyOpenSettings): _settings.HotkeyOpenSettings = spec; SettingsHotkeyText.Text = spec; break;
            case nameof(AppSettings.HotkeyManualCapture): _settings.HotkeyManualCapture = spec; ManualHotkeyText.Text = spec; break;
        }
    }

    // ---- Profiles ----

    private void RefreshProfiles() => ProfileList.ItemsSource = _repo.GetAll().ToList();

    private GameProfile? SelectedProfile => ProfileList.SelectedItem as GameProfile;

    private void ProfileNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileEditWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _repo.Create(dlg.Result);
            RefreshProfiles();
        }
    }

    private void ProfileEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } sel) return;
        var current = _repo.GetById(sel.Id);
        if (current is null) return;
        var dlg = new ProfileEditWindow(current) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _repo.Update(dlg.Result);
            RefreshProfiles();
        }
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } sel) return;
        var ok = MessageBox.Show(this, $"Delete profile \"{sel.Name}\"?", "Confirm",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;
        _repo.Delete(sel.Id);
        RefreshProfiles();
    }

    // ---- Save / Cancel ----

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 100)
        {
            MessageBox.Show(this, "Capture interval must be a number at least 100 ms.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            IntervalBox.Focus();
            return;
        }

        var s = _settings;
        s.BaseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? null : BaseUrlBox.Text.Trim();
        s.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? null : ModelBox.Text.Trim();
        if (!string.IsNullOrEmpty(ApiKeyBox.Password)) s.ApiKey = ApiKeyBox.Password; // empty → keep existing
        s.SourceLang = GetComboValue(SourceLangBox, "auto");
        s.TargetLang = GetComboValue(TargetLangBox, "id");
        s.CaptureIntervalMs = interval;
        var ocr = OcrEngineKind.Tesseract;
        if (OcrEngineBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            Enum.TryParse(tag, out ocr);
        s.OcrEngine = ocr;
        s.OverlayFontFamily = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? "Segoe UI" : FontFamilyBox.Text.Trim();
        s.OverlayFontSize = FontSizeSlider.Value;
        s.OverlayTextColor = string.IsNullOrWhiteSpace(TextColorBox.Text) ? "#FFFFFF" : TextColorBox.Text.Trim();
        s.OverlayBgColor = string.IsNullOrWhiteSpace(BgColorBox.Text) ? "#CC000000" : BgColorBox.Text.Trim();
        s.OverlayOpacity = OpacitySlider.Value;

        new SettingsStore().Save(s);
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Opened via Show() (non-modal) — never set DialogResult here: WPF throws on a
        // non-dialog window and kills the app. Cancel = discard + close.
        Close();
    }

    // ---- helpers ----

    private static void SelectCombo(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.Text = value;
    }

    private static string GetComboValue(ComboBox box, string fallback)
        => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? box.Text?.Trim() ?? fallback;

    private void SelectOcrEngine(OcrEngineKind kind)
    {
        for (int i = 0; i < OcrEngineBox.Items.Count; i++)
        {
            if (OcrEngineBox.Items[i] is ComboBoxItem cbi && cbi.Tag is string s
                && Enum.TryParse<OcrEngineKind>(s, out var parsed) && parsed == kind)
            {
                OcrEngineBox.SelectedIndex = i;
                return;
            }
        }
    }
}
