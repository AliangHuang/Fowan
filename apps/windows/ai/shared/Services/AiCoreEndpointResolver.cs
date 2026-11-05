namespace Fowan.Ai.Shared.Services;

internal static class AiCoreEndpointResolver
{
#if FOWAN_DEVELOPMENT_RUNTIME
    internal const string ExecutableName = "fowan-core.Dev.exe";
#else
    internal const string ExecutableName = "fowan-core.exe";
#endif

    public static string? ResolveExecutablePath() => ResolveExecutablePath(AppContext.BaseDirectory);

    internal static string? ResolveExecutablePath(string baseDirectory)
    {
        var explicitPath = Environment.GetEnvironmentVariable("FOWAN_CORE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        baseDirectory = Path.GetFullPath(baseDirectory);
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, "Core", ExecutableName),
            Path.Combine(baseDirectory, ExecutableName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "Core", ExecutableName))
        };
        var directory = new DirectoryInfo(baseDirectory);
        for (var level = 0; level < 7 && directory is not null; level++, directory = directory.Parent)
        {
            // Tools live below the unified app root (for example, app\Tools\Report),
            // while the bundled Core always lives directly under app\Core.
            candidates.Add(Path.Combine(directory.FullName, "Core", ExecutableName));
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "out", "core", "windows", "win-x64", "debug", "fowan-core.exe"));
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "out", "core", "windows", "win-x64", "release", "fowan-core.exe"));
        }

        var resolved = candidates.FirstOrDefault(File.Exists);
        return resolved is null ? null : Path.GetFullPath(resolved);
    }

    public static string ResolvePipeName()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fowan",
            "Core");
        Directory.CreateDirectory(root);
        var tokenPath = Path.Combine(root, "pipe-token");
        string token;
        try
        {
            token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : string.Empty;
            if (token.Length != 32 || token.Any(character => !Uri.IsHexDigit(character)))
            {
                token = Guid.NewGuid().ToString("N");
                File.WriteAllText(tokenPath, token);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AiCoreException(
                "secret_store_unavailable",
                "The per-user Core endpoint could not be initialized.");
        }

        return $"fowan-core-v1-{token}";
    }
}
