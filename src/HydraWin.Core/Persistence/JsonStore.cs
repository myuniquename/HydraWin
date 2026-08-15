namespace HydraWin.Core.Persistence;

// Temporary suppression: this type is an empty placeholder by design, which is also why its type
// parameter is unused. Task 04 gives it Load/Save over <typeparamref name="T"/> and deletes this
// pragma pair with it.
#pragma warning disable S2094 // Classes should not be empty
#pragma warning disable S2326 // Unused type parameters should be removed

/// <summary>
/// Atomic load-or-default / save for a JSON-backed document. Placeholder — task 04 implements it
/// and task 05 reuses it for the recovery journal rather than writing a second persistence
/// mechanism.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
/// <remarks>
/// <para>
/// <b>Take the file path as a constructor argument.</b> Tasks 04 and 05 both test this against
/// temporary directories, so <c>%APPDATA%\HydraWin\</c> must be known only to the concrete stores
/// (<c>WorkspaceStore</c>, <c>RecoveryJournal</c>) — never hardcoded in here.
/// </para>
/// <para>
/// Saving is atomic: serialize to <c>&lt;name&gt;.tmp</c> beside the target, then
/// <c>File.Replace</c> (falling back to <c>File.Move(overwrite: true)</c> when the target does not
/// exist yet). On a deserialization failure, rename the bad file to
/// <c>&lt;name&gt;.corrupt-&lt;yyyyMMdd-HHmmss&gt;</c> and start from defaults — never crash, and
/// never silently overwrite the evidence.
/// </para>
/// </remarks>
public sealed class JsonStore<T>
    where T : class, new()
{
}

#pragma warning restore S2326 // Unused type parameters should be removed
#pragma warning restore S2094 // Classes should not be empty
