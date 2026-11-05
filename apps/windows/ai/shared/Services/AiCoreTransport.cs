using System.IO.Pipes;

namespace Fowan.Ai.Shared.Services;

public interface IAiCoreTransport : IDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(int timeoutMilliseconds, CancellationToken cancellationToken);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

public interface IAiCoreTransportFactory
{
    IAiCoreTransport Create(string pipeName);
}

internal sealed class NamedPipeAiCoreTransport(string pipeName) : IAiCoreTransport
{
    private readonly NamedPipeClientStream _pipe = new(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);

    public bool IsConnected => _pipe.IsConnected;

    public Task ConnectAsync(int timeoutMilliseconds, CancellationToken cancellationToken) =>
        _pipe.ConnectAsync(timeoutMilliseconds, cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _pipe.ReadAsync(buffer, cancellationToken);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        _pipe.WriteAsync(buffer, cancellationToken);

    public Task FlushAsync(CancellationToken cancellationToken) =>
        _pipe.FlushAsync(cancellationToken);

    public void Dispose() => _pipe.Dispose();
}

internal sealed class NamedPipeAiCoreTransportFactory : IAiCoreTransportFactory
{
    public IAiCoreTransport Create(string pipeName) => new NamedPipeAiCoreTransport(pipeName);
}
