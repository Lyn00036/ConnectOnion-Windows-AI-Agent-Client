using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ConnectOnion.WinUIClient.Common;

/// <summary>
/// Binds an absolute local file path (attachment thumbnail source, either a
/// picked outgoing file or a cached incoming image) to a <see cref="BitmapImage"/>.
/// Returns null for an empty/invalid path so the bound <c>Image</c> control simply
/// renders nothing rather than throwing during XAML data binding.
/// </summary>
public sealed class LocalPathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return new BitmapImage(new Uri(path));
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
