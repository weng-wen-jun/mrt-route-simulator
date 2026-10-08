using System.IO.Pipes;
using System.Buffers.Binary;
using System.Text.Json;

namespace MrtRouteSimulator.Automation;

public sealed record DesktopRequest(string Command, JsonElement Arguments);
public sealed record DesktopResponse(bool Success, JsonElement? Result, string? Error = null);

/// <summary>Length-prefixed local IPC, never a network listener or a script execution endpoint.</summary>
public static class DesktopBridge
{
    public const int MaximumMessageBytes = 4_000_000;
    public static string PipeName(int processId) => "mrt-route-simulator-mcp-" + processId;

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AutomationJson.Options);
        if (bytes.Length > MaximumMessageBytes) throw new InvalidOperationException("橋接回應超過大小上限；請使用分頁。");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is < 1 or > MaximumMessageBytes) throw new InvalidDataException("橋接訊息大小無效。");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return JsonSerializer.Deserialize<T>(bytes, AutomationJson.Options)
            ?? throw new InvalidDataException("橋接訊息無效。");
    }

    public static async Task<JsonElement> CallAsync(int processId, DesktopRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("桌面橋接需要 Windows。");
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await using var pipe = new NamedPipeClientStream(".", PipeName(processId), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, timeout.Token);
        await WriteAsync(pipe, request, timeout.Token);
        var response = await ReadAsync<DesktopResponse>(pipe, timeout.Token);
        if (!response.Success) throw new InvalidOperationException(response.Error);
        return response.Result ?? JsonSerializer.SerializeToElement(new { }, AutomationJson.Options);
    }
}

/// <summary>Opt-in server for one WPF process; requests are serialized before dispatch.</summary>
public sealed class DesktopBridgeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public Task Completion => _loop;
    public DesktopBridgeServer(Func<DesktopRequest, CancellationToken, Task<object>> handler)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _loop = RunAsync(handler);
    }

    private async Task RunAsync(Func<DesktopRequest, CancellationToken, Task<object>> handler)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(DesktopBridge.PipeName(Environment.ProcessId),
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                DesktopResponse response;
                try
                {
                    var request = await DesktopBridge.ReadAsync<DesktopRequest>(pipe, timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                    var value = await handler(request, timeout.Token);
                    response = new(true, JsonSerializer.SerializeToElement(value, AutomationJson.Options));
                }
                catch (Exception exception)
                {
                    response = new(false, null, exception.Message);
                }
                try
                {
                    await DesktopBridge.WriteAsync(pipe, response, timeout.Token);
                }
                catch (InvalidOperationException exception)
                {
                    // Size checks happen before any bytes are written, so a small error is safe.
                    await DesktopBridge.WriteAsync(pipe, new DesktopResponse(false, null, exception.Message), timeout.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (IOException) { /* A disconnected client must not disable the bridge. */ }
            catch (OperationCanceledException) { /* Idle clients are bounded. */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop; }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
