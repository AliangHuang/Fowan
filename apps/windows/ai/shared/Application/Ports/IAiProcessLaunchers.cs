namespace Fowan.Ai.Shared.Application.Ports;

using Fowan.Ai.Shared.Services;

public interface IAiCoreProcessLauncher
{
    void Start(string executablePath);
}

public interface IAiApplicationLauncher
{
    void Launch(AiApplication application, params string[] arguments);
}
