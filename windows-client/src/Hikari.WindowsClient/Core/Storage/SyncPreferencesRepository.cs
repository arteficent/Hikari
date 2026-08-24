namespace Hikari.WindowsClient.Core.Storage;

/// <summary>
/// Tracks which server items the user has marked for local sync, and what each
/// marked item is called on disk. Mirrors
/// <c>android-client/app/src/core/storage/SyncPreferencesRepository.kt</c>.
///
/// <para><b>syncIds</b> is the desired state: every item the user has ticked.</para>
/// <para><b>syncIndex</b> is the actual state: id → library-relative path of the
/// file currently on disk. Sync reconciles the second towards the first.</para>
/// </summary>
public sealed class SyncPreferencesRepository : JsonPreferenceStore<SyncPreferencesRepository.SyncState>
{
    public sealed class SyncState
    {
        public HashSet<string> SyncIds { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> SyncIndex { get; set; } = new(StringComparer.Ordinal);
        public string? LastSyncIso { get; set; }
    }

    public SyncPreferencesRepository() : base("sync.json") { }

    public IReadOnlySet<string> SyncIds => Read(s => new HashSet<string>(s.SyncIds, StringComparer.Ordinal));

    public IReadOnlyDictionary<string, string> SyncIndex =>
        Read(s => new Dictionary<string, string>(s.SyncIndex, StringComparer.Ordinal));

    public string? LastSyncIso => Read(s => s.LastSyncIso);

    public bool IsMarked(string id) => Read(s => s.SyncIds.Contains(id));

    public string? LocalPathFor(string id) =>
        Read(s => s.SyncIndex.TryGetValue(id, out var v) ? v : null);

    public void SetSyncEnabled(string id, bool enabled)
    {
        AppLog.Debug($"SyncPreferences.SetSyncEnabled(id={id}, enabled={enabled})");
        Mutate(s =>
        {
            if (enabled) s.SyncIds.Add(id);
            else s.SyncIds.Remove(id);
        });
    }

    public void SetSyncEntry(string id, string relativePath)
    {
        AppLog.Debug($"SyncPreferences.SetSyncEntry(id={id}, path={relativePath})");
        Mutate(s => s.SyncIndex[id] = relativePath);
    }

    public void RemoveSyncEntry(string id)
    {
        AppLog.Debug($"SyncPreferences.RemoveSyncEntry(id={id})");
        Mutate(s => s.SyncIndex.Remove(id));
    }

    public void SetLastSync(string isoTime) => Mutate(s => s.LastSyncIso = isoTime);
}
