using System.Globalization;
using System.Windows.Data;

namespace GameSubTranslate.App;

/// <summary>
/// F59: returns the first letter of the profile name (uppercase) for the avatar circle.
/// Empty/null → "?" so the avatar is never blank.
/// </summary>
public sealed class ProfileAvatarConverter : IValueConverter
{
    public static readonly ProfileAvatarConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
            return char.ToUpperInvariant(s[0]).ToString();
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
