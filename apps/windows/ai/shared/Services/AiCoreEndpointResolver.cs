namespace Fowan.Ai.Shared.Services;

internal static class AiCoreEndpointResolver
{
    public static string? ResolveExecutablePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("FOWAN_CORE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, "Core", "fowan-core.exe"),
            Path.Combine(baseDirectory, "fowan-core.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "Core", "fowan-core.exe"))
        };
        var directory = new DirectoryInfo(baseDirectory);
        for (var level = 0; level < 7 && directory is not null; level++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "out", "core", "windows", "win-x64", "debug", "fowan-core.exe"));
            candidates.Add(Path.Combine(directory.FullName, "FowanCore", "out", "core", "windows", "win-x64", "release", "fowan-core.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
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
