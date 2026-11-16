using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GivreControl;

public static class ControlPipeClient
{
    public static string DefaultPipeName => "GivreWork-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];

    public static string PipeName => ResolvePipeName(Environment.GetEnvironmentVariable("GIVRE_CONTROL_PIPE"),
        Environment.GetEnvironmentVariable("GIVRE_CLONE_RUN_NAME"));

    public static string ResolvePipeName(string? explicitName, string? runName) => explicitName
        ?? (string.IsNullOrEmpty(runName) ? DefaultPipeName : DefaultPipeName + "-qa-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runName)))[..16]);

    public static async Task<JsonElement> CallAsync(string tool, object arguments, CancellationToken ct)
    {
        string pipeName = PipeName;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(3000, ct); }
        catch (TimeoutException) { throw new InvalidOperationException("GivreWork is not reachable. Open GivreWork and enable its local MCP bridge."); }
        byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { tool, arguments });
        await ControlFrame.WriteAsync(pipe, request, ct);
        using var response = JsonDocument.Parse(await ControlFrame.ReadAsync(pipe, ct));
        var root = response.RootElement;
        if (root.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException(root.GetProperty("error").GetString() ?? "GivreWork rejected the request.");
        return root.Clone();
    }
}

public static class ControlFrame
{
    public const int MaxBytes = 2_000_000;
    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaxBytes) throw new InvalidDataException("Control message size is invalid.");
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        return body;
    }

    public static async Task WriteAsync(Stream stream, byte[] body, CancellationToken ct)
    {
        if (body.Length is < 1 or > MaxBytes) throw new InvalidDataException("Control message size is invalid.");
        byte[] header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}

public sealed record ControlRequest(string Tool, JsonElement Arguments);
