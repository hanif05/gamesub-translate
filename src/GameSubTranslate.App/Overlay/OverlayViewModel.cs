using System.ComponentModel;
using System.Text;

namespace GameSubTranslate.App.Overlay;

/// <summary>Holds the currently displayed subtitle text. Text survives show/hide (T19).</summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private string _text = "";
    // T36: streaming token buffer — accumulates tokens for the current translation pass.
    // Resets on BeginStream/ShowText/Clear so a new subtitle doesn't append to the previous one.
    private StringBuilder? _streamBuffer;

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

    /// <summary>T36: start a new streaming pass. Wipes the prior text and the token buffer.</summary>
    public void BeginStream()
    {
        _streamBuffer = new StringBuilder();
        Text = "";
    }

    /// <summary>T36: append a single token to the current streaming pass. No-op if no pass started.</summary>
    public void AppendToken(string token)
    {
        if (_streamBuffer is null) return;
        _streamBuffer.Append(token);
        Text = _streamBuffer.ToString();
    }

    /// <summary>T36: close the current streaming pass. Subsequent tokens are ignored until the next BeginStream.</summary>
    public void EndStream() => _streamBuffer = null;
}
