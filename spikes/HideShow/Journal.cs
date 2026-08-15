using System.Text;
using System.Text.Json;

namespace HideShow;

/// <summary>
/// Write-ahead mini-journal. The spike hides windows before HydraWin's real recovery journal
/// (task 05) exists, so every hide is recorded to disk and flushed <b>before</b> SW_HIDE, and the
/// entry is removed only after a verified re-show. `hideshow rescue` replays whatever is left.
/// </summary>
public sealed record HiddenEntry(
    long Hwnd,
    string Title,
    string Process,
    uint Pid,
    int Flags,
    int ShowCmd,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int NormLeft,
    int NormTop,
    int NormRight,
    int NormBottom,
    string HiddenAtUtc,
    int OwnerPid)
{
    public Native.WINDOWPLACEMENT ToPlacement()
    {
        var wp = Native.WINDOWPLACEMENT.Create();
        wp.flags = Flags;
        wp.showCmd = ShowCmd;
        wp.ptMinPosition = new Native.POINT { X = MinX, Y = MinY };
        wp.ptMaxPosition = new Native.POINT { X = MaxX, Y = MaxY };
        wp.rcNormalPosition = new Native.RECT
        {
            Left = NormLeft,
            Top = NormTop,
            Right = NormRight,
            Bottom = NormBottom,
        };
        return wp;
    }

    public static HiddenEntry From(IntPtr hwnd, string title, string process, uint pid,
        in Native.WINDOWPLACEMENT wp) =>
        new(hwnd.ToInt64(), title, process, pid, wp.flags, wp.showCmd,
            wp.ptMinPosition.X, wp.ptMinPosition.Y, wp.ptMaxPosition.X, wp.ptMaxPosition.Y,
            wp.rcNormalPosition.Left, wp.rcNormalPosition.Top,
            wp.rcNormalPosition.Right, wp.rcNormalPosition.Bottom,
            DateTime.UtcNow.ToString("O"), Environment.ProcessId);
}

public static class Journal
{
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HydraWin",
        "spike-hidden.jsonl");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Appends and physically flushes the entry. Must complete before SW_HIDE.</summary>
    public static void Append(HiddenEntry entry)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string line = JsonSerializer.Serialize(entry, Options) + "\n";
        using var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read);
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true);
    }

    public static List<HiddenEntry> ReadAll()
    {
        var result = new List<HiddenEntry>();
        if (!File.Exists(Path))
        {
            return result;
        }

        foreach (string line in File.ReadAllLines(Path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                HiddenEntry? entry = JsonSerializer.Deserialize<HiddenEntry>(line, Options);
                if (entry is not null)
                {
                    result.Add(entry);
                }
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"  journal: unparsable line skipped ({ex.Message})");
            }
        }

        return result;
    }

    public static void Rewrite(IEnumerable<HiddenEntry> entries)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var sb = new StringBuilder();
        foreach (HiddenEntry e in entries)
        {
            sb.Append(JsonSerializer.Serialize(e, Options)).Append('\n');
        }

        using var fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true);
    }
}
