namespace Fowan.Windows.Platform.Windows;

internal static class ToolExecutableResolver
{
    public static string? ResolveTodo() => Resolve(
        "Fowan.Todo.Windows.exe", "Todo", "windows-todo");

    public static string? ResolveDiary() => Resolve(
        "Fowan.Diary.Windows.exe", "Diary", "windows-diary");

    public static string? ResolveAi(string executableName, string outputDirectory, string installedDirectory) =>
        Resolve(executableName, Path.Combine("AI", installedDirectory), outputDirectory);

    private static string? Resolve(string executableName, string installedDirectory, string outputDirectory)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, executableName),
            Path.Combine(baseDirectory, "Tools", installedDirectory, executableName)
        };
        var repoRoot = FindRepoRoot(baseDirectory);
        if (repoRoot is not null)
        {
            foreach (var configuration in BuildConfigurationCandidates(baseDirectory))
            {
                candidates.Add(Path.Combine(
                    repoRoot, "out", outputDirectory, configuration.ToLowerInvariant(), executableName));
            }
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory is not null && depth < 12; depth++)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Fowan.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static IReadOnlyList<string> BuildConfigurationCandidates(string baseDirectory)
    {
        var configurations = new List<string>();
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            if (directory.Name.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.Equals("Release", StringComparison.OrdinalIgnoreCase))
            {
                configurations.Add(directory.Name.Equals("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug");
                break;
            }
            directory = directory.Parent;
        }
        if (configurations.Count == 0) configurations.Add("Debug");
        configurations.Add("Release");
        configurations.Add("Debug");
        return configurations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
