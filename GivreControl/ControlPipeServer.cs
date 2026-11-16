using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GivreControl;

public sealed class ControlPipeServer : IAsyncDisposable
{
    private readonly string _name;
    private readonly Func<ControlRequest, CancellationToken, Task<JsonNode>> _handle;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(8);
    private readonly ConcurrentDictionary<Guid, Task> _clients = new();
    private Task? _loop;

    public ControlPipeServer(string name, Func<ControlRequest, CancellationToken, Task<JsonNode>> handle)
        => (_name, _handle) = (name, handle);

    public void Start()
    {
        if (_loop is not null) throw new InvalidOperationException("Control bridge already started.");
        _loop = AcceptAsync();
    }

    public void Stop() => _stop.Cancel();

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await _slots.WaitAsync(_stop.Token); }
            catch (OperationCanceledException) { break; }
            var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(_stop.Token); }
            catch (OperationCanceledException) { pipe.Dispose(); _slots.Release(); break; }
            catch { pipe.Dispose(); _slots.Release(); throw; }
            var id = Guid.NewGuid();
            var task = ServeAsync(pipe);
            _clients[id] = task;
            _ = task.ContinueWith(_ => { _clients.TryRemove(id, out var ignored); _slots.Release(); }, TaskScheduler.Default);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var request = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            request.CancelAfter(TimeSpan.FromSeconds(65));
            Task? disconnected = null;
            try
            {
                using var document = JsonDocument.Parse(await ControlFrame.ReadAsync(pipe, request.Token));
                var root = document.RootElement;
                string tool = root.GetProperty("tool").GetString() ?? "";
                var args = root.GetProperty("arguments").Clone();
                disconnected = CancelOnDisconnect(pipe, request);
                var result = await _handle(new ControlRequest(tool, args), request.Token);
                await ControlFrame.WriteAsync(pipe, Encoding.UTF8.GetBytes(result.ToJsonString()), request.Token);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or IOException or TimeoutException or FormatException or OverflowException)
            {
                if (pipe.IsConnected && !request.IsCancellationRequested)
                {
                    var error = new JsonObject { ["isError"] = true, ["error"] = ex is IOException ? "Local control I/O failed." : ex.Message };
                    try { await ControlFrame.WriteAsync(pipe, Encoding.UTF8.GetBytes(error.ToJsonString()), request.Token); }
                    catch (IOException) { }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                request.Cancel();
                if (disconnected is not null) await disconnected;
            }
        }
    }

    private static async Task CancelOnDisconnect(Stream pipe, CancellationTokenSource request)
    {
        try
        {
            await pipe.ReadAsync(new byte[1], request.Token);
            request.Cancel();
        }
        catch (OperationCanceledException) { }
        catch (IOException) { request.Cancel(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null) await _loop;
        await Task.WhenAll(_clients.Values.ToArray());
        _slots.Dispose();
        _stop.Dispose();
    }
}
