namespace Fowan.Todo.Windows.Presentation;

internal sealed class TodoWindowPresentationController
{
    private int _modalSurfaceCount;
    private int _titleBarGeneration;

    public bool HasModalSurface => _modalSurfaceCount > 0;

    public int NextTitleBarGeneration() => ++_titleBarGeneration;

    public bool IsCurrentTitleBarGeneration(int generation) => generation == _titleBarGeneration;

    public void InvalidateTitleBarGeneration() => _titleBarGeneration++;

    public void EnterModalSurface() => _modalSurfaceCount++;

    public bool ExitModalSurface()
    {
        _modalSurfaceCount = Math.Max(0, _modalSurfaceCount - 1);
        return _modalSurfaceCount == 0;
    }
}
