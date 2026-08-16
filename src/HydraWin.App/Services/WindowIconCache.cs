using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HydraWin.Core.Interop;

namespace HydraWin.App.Services;

/// <summary>
/// Turns a process image path into an icon for a window row, once per path.
/// </summary>
/// <remarks>
/// Extraction is the expensive part and every window of an app shares an image path, so results —
/// including "this one has no icon" — are cached by path. The icon handle is released as soon as
/// WPF has copied the pixels out of it.
/// </remarks>
public sealed class WindowIconCache
{
    private readonly IIconSource source;
    private readonly Dictionary<string, ImageSource?> cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a cache over an icon source.</summary>
    public WindowIconCache(IIconSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
    }

    /// <summary>
    /// The app icon for a process image path, or <see langword="null"/> when there is none — an
    /// empty path means a protected process, which the row shows with a generic glyph.
    /// </summary>
    public ImageSource? GetIcon(string processPath)
    {
        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        if (cache.TryGetValue(processPath, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? icon = Extract(processPath);
        cache[processPath] = icon;
        return icon;
    }

    private ImageSource? Extract(string processPath)
    {
        if (!source.TryGetIcon(processPath, out nint hIcon))
        {
            return null;
        }

        try
        {
            ImageSource icon = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Frozen so it can be shared across rows and drawn on any thread.
            icon.Freeze();
            return icon;
        }
        catch (Win32Exception)
        {
            // A malformed or unreadable icon resource. A missing icon is not worth a failure.
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            source.DestroyIcon(hIcon);
        }
    }
}
