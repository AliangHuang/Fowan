using System.Diagnostics;
using Fowan.Ai.Shared.Application.Ports;
using Fowan.Ai.Shared.Services;

namespace Fowan.Ai.Config.Windows.Platform.Windows;

internal sealed class WindowsAiCoreProcessLauncher : IAiCoreProcessLauncher
{
    public void Start(string executablePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}

internal sealed class WindowsAiApplicationLauncher : IAiApplicationLauncher
{
    public void Launch(AiApplication application, params string[] arguments)
    {
        var executable = AiApplicationPathResolver.ResolveExecutable(application) ??
            throw new FileNotFoundException($"{AiApplicationPathResolver.ExecutableName(application)} was not found.");
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
}
