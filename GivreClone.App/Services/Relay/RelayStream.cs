using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Relay;

/// <summary>Un flux GivreRelay vu comme une connexion TCP : lecture = données reçues du gateway pour ce stream_id,
/// écriture = messages binaires [stream_id][données]. Fin de lecture (0) quand le gateway ferme le flux.</summary>
public sealed class RelayStream : Stream
{
    private readonly GivreRelayClient _client;
    private readonly Channel<byte[]> _in = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private byte[]? _cur;
    private int _off;
    private int _closed;

    public uint Id { get; }
    /// <summary>Raison de fermeture donnée par le gateway (quota_exceeded…), ou null.</summary>
    public string? CloseReason { get; private set; }

    internal RelayStream(GivreRelayClient client, uint id) { _client = client; Id = id; }

    internal void Push(byte[] data) => _in.Writer.TryWrite(data);

    internal void RemoteClosed(string? reason)
    {
        CloseReason ??= reason;
        Interlocked.Exchange(ref _closed, 1);
        _in.Writer.TryComplete();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_cur is null || _off >= _cur.Length)
        {
            if (!await _in.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return 0;
            if (_in.Reader.TryRead(out var next)) { _cur = next; _off = 0; }
        }
        int n = Math.Min(buffer.Length, _cur.Length - _off);
        _cur.AsMemory(_off, n).CopyTo(buffer);
        _off += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _closed) != 0) throw new IOException("flux relais fermé");
        if (buffer.Length > 0) await _client.SendDataAsync(Id, buffer, ct).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0) _ = _client.CloseStreamAsync(Id);
        _in.Writer.TryComplete();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
