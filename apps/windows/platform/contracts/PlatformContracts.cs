namespace Fowan.Windows.Platform.Contracts;

public sealed record PlatformOperationResult(bool Succeeded, string? Error = null)
{
    public static PlatformOperationResult Success() => new(true);

    public static PlatformOperationResult Failure(string error) => new(false, error);
}

public sealed record ProcessLaunchRequest(
    string Target,
    bool UseShellExecute = true,
    bool Elevated = false,
    string? WorkingDirectory = null);

public sealed record FileOpenRequest(
    IReadOnlyCollection<string> Extensions,
    string? SuggestedStartLocation = null);

public sealed record TextFileSaveRequest(
    string SuggestedFileName,
    string Content,
    string DisplayName,
    string Extension);

public interface IUiDispatcher
{
    bool TryEnqueue(Action action);

    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);
}

public sealed class UiDispatcherUnavailableException()
    : InvalidOperationException("The UI dispatcher is unavailable.");

public interface IProcessLauncher
{
    PlatformOperationResult Launch(ProcessLaunchRequest request);
}

public interface IClipboardService
{
    PlatformOperationResult SetText(string text);
}

public interface IFileDialogService
{
    Task<string?> PickOpenFileAsync(FileOpenRequest request, CancellationToken cancellationToken = default);

    Task<PlatformOperationResult> SaveTextFileAsync(
        TextFileSaveRequest request,
        CancellationToken cancellationToken = default);
}
