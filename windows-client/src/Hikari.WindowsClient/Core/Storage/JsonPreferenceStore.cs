using System.Text.Json;

namespace Hikari.WindowsClient.Core.Storage;

/// <summary>
/// Where the client keeps its state. Preferences live under
/// <c>%LOCALAPPDATA%\Hikari</c>; downloaded content defaults to
/// <c>%USERPROFILE%\Hikari</c> — the desktop analogue of the android client's
/// <c>/sdcard/Hikari</c> — and is user-configurable.
/// </summary>
public static class AppPaths
{
    public static string PreferencesDirectory { get; } = EnsureDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hikari"));

    public static string DefaultLibraryRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Hikari");

    public static string PreferenceFile(string name) => Path.Combine(PreferencesDirectory, name);

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

/// <summary>
/// Tiny JSON-file preference store: load once, mutate in memory, write atomically.
/// Plays the role of android's DataStore, minus the coroutine Flow plumbing —
/// WinUI consumers subscribe to <see cref="Changed"/> instead.
/// </summary>
public abstract class JsonPreferenceStore<TState> where TState : class, new()
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly Lock _gate = new();

    protected TState State { get; private set; }

    /// <summary>Raised after any successful mutation, on the mutating thread.</summary>
    public event EventHandler? Changed;

    protected JsonPreferenceStore(string fileName)
    {
        _filePath = AppPaths.PreferenceFile(fileName);
        State = Load();
    }

    private TState Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    return JsonSerializer.Deserialize<TState>(json, SerializerOptions) ?? new TState();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to read preferences from {_filePath}: {ex.Message}");
        }

        return new TState();
    }

    protected void Mutate(Action<TState> mutation)
    {
        lock (_gate)
        {
            mutation(State);
            Persist();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected T Read<T>(Func<TState, T> selector)
    {
        lock (_gate)
        {
            return selector(State);
        }
    }

    private void Persist()
    {
        try
        {
            AppPaths.EnsureDirectory(AppPaths.PreferencesDirectory);
            var json = JsonSerializer.Serialize(State, SerializerOptions);

            // Write to a sibling temp file then move, so a crash mid-write can never
            // leave a truncated preferences file behind.
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to persist preferences to {_filePath}: {ex.Message}");
        }
    }
}
