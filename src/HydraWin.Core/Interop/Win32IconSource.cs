namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="IIconSource"/>: a thin adapter onto <c>NativeMethods</c>, which is the only
/// class allowed to declare P/Invoke.
/// </summary>
public sealed class Win32IconSource : IIconSource
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32IconSource Instance { get; } = new();

    /// <inheritdoc />
    public bool TryGetIcon(string processPath, out nint hIcon) =>
        NativeMethods.TryExtractSmallIcon(processPath, out hIcon);

    /// <inheritdoc />
    public void DestroyIcon(nint hIcon) => NativeMethods.DestroyIcon(hIcon);
}
