using System.Runtime.CompilerServices;

// Every test shares one preferences directory, so the JSON stores must not be hit concurrently.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Hikari.WindowsClient.Tests;

internal static class TestEnvironment
{
    public static string DataDirectory { get; private set; } = string.Empty;

#pragma warning disable CA2255 // Must run before AppPaths' static initialiser reads the variable.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "hikari-windows-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        Environment.SetEnvironmentVariable("HIKARI_DATA_DIR", DataDirectory);
    }

    public static string NewLibraryRoot()
    {
        var root = Path.Combine(DataDirectory, "library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
