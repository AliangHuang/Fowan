using System.Diagnostics;

namespace Fowan.Ai.Shared.Services;

public enum AiApplication
{
    Chat,
    Config
}
public static class AiApplicationLauncher
{
    public const string ChatExecutableName = "Fowan.Ai.Chat.Windows.exe";
    public const string ConfigExecutableName = "Fowan.Ai.Config.Windows.exe";

    public static void Launch(AiApplication application, params string[] arguments)
    {
        var executable = ResolveExecutable(application) ??
            throw new FileNotFoundException($"{ExecutableName(application)} was not found.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        };
        foreach (var argument in arguments.Where(argument => !string.IsNullOrWhiteSpace(argument)))
        {
            startInfo.ArgumentList.Add(argument);
        }
        Process.Start(startInfo);
    }

    public static string? ResolveExecutable(AiApplication application)
    {
        var explicitPath = Environment.GetEnvironmentVariable(
            application == AiApplication.Chat ? "FOWAN_AI_CHAT_PATH" : "FOWAN_AI_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var executableName = ExecutableName(application);
        var toolDirectory = application == AiApplication.Chat ? "Chat" : "Config";
        var outputDirectory = application == AiApplication.Chat ? "windows-ai-chat" : "windows-ai-config";
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, executableName),
            Path.Combine(baseDirectory, "Tools", "AI", toolDirectory, executableName),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", toolDirectory, executableName))
        };
        var directory = new DirectoryInfo(baseDirectory);
        for (var level = 0; level < 8 && directory is not null; level++, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "out", outputDirectory, "debug", executableName));
            candidates.Add(Path.Combine(directory.FullName, "out", outputDirectory, "release", executableName));
            candidates.Add(Path.Combine(directory.FullName, "Tools", "AI", toolDirectory, executableName));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ExecutableName(AiApplication application) =>
        application == AiApplication.Chat ? ChatExecutableName : ConfigExecutableName;
}
