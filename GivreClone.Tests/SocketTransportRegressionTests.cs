using System;
using System.IO;
using System.Threading.Channels;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline transport contract checks. These inspect the production wiring because the pure
/// test assembly intentionally does not load the Avalonia application or Dofus transport.
/// They do NOT validate a live login or server ACK.
/// </summary>
public sealed class SocketTransportRegressionTests
{
    private static string Source(string file)
    {
        var root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName,
            "GivreClone.App", "Services", "Transport", "Socket", file)));
        if (root is null) throw new DirectoryNotFoundException("Repository root missing");
        return File.ReadAllText(Path.Combine(root.FullName,
            "GivreClone.App", "Services", "Transport", "Socket", file));
    }

    [Fact]
    public void Injector_is_bounded_and_fails_closed_when_disconnected()
    {
        var client = Source("SocketGameClient.cs");
        Assert.Contains("Channel.CreateBounded<Outgoing>(new BoundedChannelOptions(256)", client);
        Assert.Contains("FullMode = BoundedChannelFullMode.Wait", client);
        Assert.Contains("Volatile.Read(ref _injectionReady) == 1", client);
        Assert.Contains("_outbound.Writer.TryWrite", client);
    }

    [Fact]
    public void Ready_only_after_character_success_and_revoked_before_unregistration()
    {
        var client = Source("SocketGameClient.cs");
        var marker = client.IndexOf("case \"CharacterSelectionEvent\":", StringComparison.Ordinal);
        var ready = client.IndexOf("Volatile.Write(ref _injectionReady, 1);", StringComparison.Ordinal);
        var close = client.LastIndexOf("Volatile.Write(ref _injectionReady, 0);", StringComparison.Ordinal);
        var unregister = client.LastIndexOf("p.UnregisterInjector?.Invoke(_logPid)", StringComparison.Ordinal);
        Assert.True(marker > 0 && ready > marker && close > ready && unregister > close);
        Assert.Contains("if (!okSel)", client);
    }

    [Fact]
    public void Never_assume_character_buckets_from_server_metadata_only()
    {
        var client = Source("SocketConnectionClient.cs");
        Assert.Contains("if (d.CharactersEmbedded) characterBuckets ??= serverMetadata;", client);
        Assert.Contains("else if (d.Type == ConnDecoded.Kind.CharacterList)", client);
        Assert.Contains("serverMetadata is not null && characterBuckets is not null", client);
    }

    [Fact]
    public void Bounded_writer_rejects_when_full_instead_of_dropping_unsent_commands()
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false,
        });
        Assert.True(channel.Writer.TryWrite(new byte[] { 1, 2 }));
        Assert.True(channel.Writer.TryWrite(new byte[] { 3, 4 }));
        Assert.False(channel.Writer.TryWrite(new byte[] { 5, 6 }));
        Assert.True(channel.Reader.TryRead(out _));
        Assert.True(channel.Writer.TryWrite(new byte[] { 7, 8 }));
    }

    [Fact]
    public void Invalid_frames_do_not_enter_injection_queue()
    {
        var client = Source("SocketGameClient.cs");
        Assert.Contains("framedToServer is { Length: > 1 and <= 2_097_152 }", client);
    }

    [Fact]
    public void Socket_outcome_does_not_call_queued_write_a_server_ack()
    {
        var client = Source("SocketGameClient.cs");
        Assert.Contains("PAS accusé serveur", client);
        // V8 extracted the real queue-drain write into a guarded method.
        // Verify BOTH the callsite and the post-Write+Flush boundary rather
        // than requiring a particular line to stay inline in the pump.
        Assert.Contains("WriteQueuedEngineActionAsync(stream!, o, p, tk)", client);
        Assert.Contains("await target.WriteAsync(item.Frame, ct)", client);
        Assert.Contains("await target.FlushAsync(ct)", client);
        Assert.Contains("public bool ServerAcknowledged => false", client);
    }
}
