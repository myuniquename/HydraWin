namespace HydraWin.Core.Interop;

/// <summary>
/// Supplies the small icon of an executable, so window rows can show the app they belong to.
/// </summary>
/// <remarks>
/// A separate seam from <see cref="IWindowApi"/> because it has nothing to do with manipulating
/// windows and nothing here is dangerous — it exists so the App layer can draw icons without
/// declaring P/Invoke of its own (repo rule), and so the icon cache is testable with a fake.
/// </remarks>
public interface IIconSource
{
    /// <summary>
    /// Extracts the small icon of an executable. The caller owns <paramref name="hIcon"/> and must
    /// release it with <see cref="DestroyIcon"/>.
    /// </summary>
    /// <returns><see langword="false"/> when there is no icon to be had.</returns>
    bool TryGetIcon(string processPath, out nint hIcon);

    /// <summary>Releases a handle obtained from <see cref="TryGetIcon"/>.</summary>
    void DestroyIcon(nint hIcon);
}
