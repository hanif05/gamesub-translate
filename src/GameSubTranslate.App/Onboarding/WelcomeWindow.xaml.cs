using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using GameSubTranslate.Config;

namespace GameSubTranslate.App.Onboarding;

/// <summary>
/// T45: 3-step first-run wizard. Updates AppSettings in-place; on Finish, marks
/// SetupCompleted=true and persists. The caller (App.OnStartup) reads the result.
/// "Skip" writes the default settings WITHOUT SetupCompleted so the user is sent
/// straight to SettingsPanel on the same run via the result flag.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly AppSettings _settings;
    private int _step = 1;

    public enum Outcome { Completed, Skipped }
    public Outcome Result { get; private set; }

    public WelcomeWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        // Pre-fill what the user already has (e.g. partial config from a previous aborted run).
        BaseUrlBox.Text = _settings.BaseUrl ?? "";
        ModelBox.Text = _settings.Model ?? "";
        SelectCombo(TargetLangBox, _settings.TargetLang);

        Hyperlink_OnRequestNavigate(this, OpenAiDocLink);
        Hyperlink_OnRequestNavigate(this, OpenRouterDocLink);

        ShowStep(1);
    }

    private static void Hyperlink_OnRequestNavigate(Window owner, Hyperlink link)
    {
        // The RequestNavigate event uses the sender's Uri; capture by name.
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch { /* swallow — link is best-effort */ }
        };
    }

    private static void SelectCombo(System.Windows.Controls.ComboBox box, string value)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is System.Windows.Controls.ComboBoxItem ci && (string)ci.Content == value)
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.IsEditable) box.Text = value;
    }

    private void ShowStep(int step)
    {
        _step = step;
        Step1.Visibility = Visibility.Collapsed;
        Step2.Visibility = Visibility.Collapsed;
        Step3.Visibility = Visibility.Collapsed;
        BackBtn.IsEnabled = step > 1;
        NextBtn.Content = step == 3 ? "Finish" : "Next";
        // F57: filled = current/done, outlined = upcoming. EA3A = filled circle, EA3B = outlined circle.
        Dot1.Text = step >= 1 ? "" : "";
        Dot2.Text = step >= 2 ? "" : "";
        Dot3.Text = step >= 3 ? "" : "";
        switch (step)
        {
            case 1:
                StepTitle.Text = "1. API setup";
                StepHint.Text = "Enter your OpenAI-compatible endpoint. You can skip and fill this in later from Settings.";
                Step1.Visibility = Visibility.Visible;
                break;
            case 2:
                StepTitle.Text = "2. Target language";
                StepHint.Text = "Pick the language you want subtitles translated into.";
                Step2.Visibility = Visibility.Visible;
                break;
            case 3:
                StepTitle.Text = "3. Quick tour";
                StepHint.Text = "Three things to know:";
                Step3.Visibility = Visibility.Visible;
                break;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 3) { ShowStep(_step + 1); return; }
        // Finish: apply current field values and persist.
        _settings.BaseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? null : BaseUrlBox.Text.Trim();
        _settings.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? null : ModelBox.Text.Trim();
        _settings.ApiKey = ApiKeyBox.Password.Length > 0 ? ApiKeyBox.Password : _settings.ApiKey;
        _settings.TargetLang = (TargetLangBox.SelectedItem is System.Windows.Controls.ComboBoxItem ci)
            ? (string)ci.Content
            : (string.IsNullOrWhiteSpace(TargetLangBox.Text) ? "id" : TargetLangBox.Text.Trim());
        _settings.SetupCompleted = true;
        Result = Outcome.Completed;
        DialogResult = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 1) ShowStep(_step - 1);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        // Persist defaults WITHOUT SetupCompleted so the caller routes the user into
        // SettingsPanel on this run. The wizard re-appears next launch because the
        // flag stayed false — but in practice once the user fills the API key and the
        // pipeline runs, they won't notice the wizard again until they reset.
        // (T44 "Reset to Defaults" will clear it explicitly when implemented in T48.)
        new SettingsStore().Save(_settings);
        Result = Outcome.Skipped;
        DialogResult = true;
    }
}
