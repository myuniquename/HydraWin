using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydraWin.Core.Persistence;

/// <summary>
/// Serializer settings shared by every <see cref="JsonStore{T}"/>.
/// </summary>
/// <remarks>
/// Non-generic on purpose: a static field on the generic type would be duplicated per closed
/// type, and these options are identical for all of them. Indented output and string enums are
/// both required — <c>state.json</c> is hand-edited by the user (tasks 08 and 09 say so).
/// </remarks>
internal static class JsonStoreOptions
{
    internal static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// Atomic load-or-default / save for a JSON-backed document.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
/// <remarks>
/// <para>
/// The file path is a constructor argument and <c>%APPDATA%\HydraWin\</c> appears nowhere in
/// here: that belongs to the concrete stores (<see cref="WorkspaceStore"/>, and task 05's
/// <c>RecoveryJournal</c>), and both their test suites point this at a temp directory.
/// </para>
/// <para>
/// <see cref="Save"/> is synchronous and never debounced. Task 05 depends on that — its
/// write-ahead journal entry must be on disk before the window it describes is hidden, which is
/// the project's one invariant. Debouncing belongs to callers that want it, as
/// <see cref="WorkspaceStore"/> does.
/// </para>
/// </remarks>
public sealed class JsonStore<T>
    where T : class, new()
{
    private readonly Lock gate = new();

    /// <summary>Creates a store over a single JSON file.</summary>
    /// <param name="path">Full path to the document; its directory is created on first save.</param>
    public JsonStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>Raised when a corrupt document was set aside and defaults were used instead.</summary>
    public event EventHandler<string>? CorruptFileQuarantined;

    /// <summary>The document's path.</summary>
    public string Path { get; }

    /// <summary>
    /// Reads the document, or returns a fresh <typeparamref name="T"/> when the file is missing.
    /// </summary>
    /// <remarks>
    /// A file that will not deserialize is renamed
    /// <c>&lt;name&gt;.corrupt-&lt;yyyyMMdd-HHmmss&gt;</c> and defaults are returned. Never crash,
    /// and never silently overwrite the evidence — the user may want to salvage it by hand.
    /// </remarks>
    public T Load()
    {
        lock (gate)
        {
            if (!File.Exists(Path))
            {
                return new T();
            }

            try
            {
                string json = File.ReadAllText(Path);
                return JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default) ?? new T();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                string quarantined = Quarantine();
                CorruptFileQuarantined?.Invoke(this, quarantined);
                return new T();
            }
        }
    }

    /// <summary>
    /// Writes the document atomically: serialize beside the target, then replace it. A reader
    /// sees either the old file or the new one, never a half-written one.
    /// </summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonStoreOptions.Default));

            if (File.Exists(Path))
            {
                // Replace keeps the destination's identity; no backup file is requested.
                File.Replace(temp, Path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, Path, overwrite: true);
            }
        }
    }

    private string Quarantine()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string target = $"{Path}.corrupt-{stamp}";

        try
        {
            File.Move(Path, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. If the bad file cannot be set aside, defaults are still the right
            // answer and the next Save replaces it; refusing to start would be worse.
        }

        return target;
    }
}
