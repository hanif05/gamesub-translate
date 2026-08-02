using System.ComponentModel;

namespace GameSubTranslate.App.Overlay;

/// <summary>Holds the currently displayed subtitle text. Text survives show/hide (T19).</summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private string _text = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public void ShowText(string text) => Text = text;

    public void Clear() => Text = "";
}
