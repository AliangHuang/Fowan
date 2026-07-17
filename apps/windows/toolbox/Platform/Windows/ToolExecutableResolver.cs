namespace Fowan.Windows.Platform.Windows;

internal static class ToolExecutableResolver
{
#if FOWAN_DEVELOPMENT_RUNTIME
    internal const string TodoExecutableName = "Fowan.Todo.Windows.Dev.exe";
    internal const string DiaryExecutableName = "Fowan.Diary.Windows.Dev.exe";
    internal const string AiChatExecutableName = "Fowan.Ai.Chat.Windows.Dev.exe";
    internal const string AiConfigExecutableName = "Fowan.Ai.Config.Windows.Dev.exe";
#else
    internal const string TodoExecutableName = "Fowan.Todo.Windows.exe";
    internal const string DiaryExecutableName = "Fowan.Diary.Windows.exe";
    internal const string AiChatExecutableName = "Fowan.Ai.Chat.Windows.exe";
    internal const string AiConfigExecutableName = "Fowan.Ai.Config.Windows.exe";
#endif

    public static string? ResolveTodo() => Resolve(
        TodoExecutableName, "Todo");

    public static string? ResolveDiary() => Resolve(
        DiaryExecutableName, "Diary");

    public static string? ResolveAi(string executableName, string installedDirectory) =>
        Resolve(executableName, Path.Combine("AI", installedDirectory));

    private static string? Resolve(string executableName, string installedDirectory)
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
            candidates.Add(Path.Combine(
                repoRoot, "build", "windows", "win-x64", "app", "Tools", installedDirectory, executableName));
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
}
