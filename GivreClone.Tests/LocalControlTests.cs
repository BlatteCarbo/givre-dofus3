using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GivreControl;
using GivreClone.App.Services.Mcp;
using Xunit;

namespace GivreClone.Tests;

public class LocalControlTests
{
    [Fact]
    public async Task Validation_profiles_route_to_separate_named_pipe_servers()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string run = Guid.NewGuid().ToString("N");
        string first = ControlPipeClient.ResolvePipeName(null, run + "-first");
        string second = ControlPipeClient.ResolvePipeName(null, run + "-second");
        Assert.NotEqual(first, second);
        Assert.NotEqual(ControlPipeClient.DefaultPipeName, first);
        Assert.Equal(first, ControlPipeClient.ResolvePipeName(null, run + "-first"));
        Assert.Equal("explicit-endpoint", ControlPipeClient.ResolvePipeName("explicit-endpoint", run));
        Assert.Equal(ControlPipeClient.DefaultPipeName, ControlPipeClient.ResolvePipeName(null, null));
        Assert.Equal(ControlPipeClient.DefaultPipeName, ControlPipeClient.ResolvePipeName(null, ""));
        await using var a = new ControlPipeServer(first, (_, _) => Task.FromResult<JsonNode>(new JsonObject { ["owner"] = "first" }));
        await using var b = new ControlPipeServer(second, (_, _) => Task.FromResult<JsonNode>(new JsonObject { ["owner"] = "second" }));
        a.Start(); b.Start();
        foreach (var (name, owner) in new[] { (first, "first"), (second, "second") })
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(3000, deadline.Token);
            await ControlFrame.WriteAsync(client, Encoding.UTF8.GetBytes("{\"tool\":\"status\",\"arguments\":{}}"), deadline.Token);
            using var response = JsonDocument.Parse(await ControlFrame.ReadAsync(client, deadline.Token));
            Assert.Equal(owner, response.RootElement.GetProperty("owner").GetString());
        }
    }

    [Fact]
    public async Task Frames_reject_oversized_input_before_reading_a_body()
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, ControlFrame.MaxBytes + 1);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => ControlFrame.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void Console_cursor_filters_and_reports_evicted_history()
    {
        var journal = new ConsoleJournal();
        for (int i = 0; i < 3005; i++) journal.Add(i % 2 == 0 ? "info" : "warning", "script", "PHASE " + i);
        var page = journal.Read(1, 2, "PHASE", "warning");
        Assert.True(page.Truncated);
        Assert.Equal(new long[] { 6, 8 }, page.Lines.Select(l => l.Cursor));
        Assert.Equal(8, page.Cursor);
    }

    [Fact]
    public async Task Console_wait_observes_new_lines_without_blocking_other_reads()
    {
        var journal = new ConsoleJournal();
        var waiting = journal.WaitAsync(0, "READY", 5, CancellationToken.None);
        journal.Add("info", "script", "progress");
        Assert.Empty(journal.Read(0, 10, "READY").Lines);
        journal.Add("success", "script", "READY");
        var result = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("READY", Assert.Single(result.Lines).Message);
    }

    [Fact]
    public async Task Named_pipe_handles_parallel_requests_and_cancels_disconnected_waits()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        string name = "GivreWork-test-" + Guid.NewGuid().ToString("N");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new ControlPipeServer(name, async (r, requestToken) =>
        {
            if (r.Tool == "wait")
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, requestToken); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            }
            return new JsonObject { ["tool"] = r.Tool };
        });
        try
        {
            server.Start();
            using var waiter = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await waiter.ConnectAsync(3000, ct);
            await ControlFrame.WriteAsync(waiter, Encoding.UTF8.GetBytes("{\"tool\":\"wait\",\"arguments\":{}}"), ct);
            await entered.Task.WaitAsync(ct);
            using (var quick = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await quick.ConnectAsync(3000, ct);
                await ControlFrame.WriteAsync(quick, Encoding.UTF8.GetBytes("{\"tool\":\"status\",\"arguments\":{}}"), ct);
                using var doc = JsonDocument.Parse(await ControlFrame.ReadAsync(quick, ct));
                Assert.Equal("status", doc.RootElement.GetProperty("tool").GetString());
            }
            waiter.Dispose();
            await cancelled.Task.WaitAsync(ct);
        }
        finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public void Script_patch_validates_before_saving_and_preserves_backup()
    {
        string root = Path.Combine(Path.GetTempPath(), "givre-scripts-" + Guid.NewGuid().ToString("N"));
        var library = new SavedLuaScripts(root);
        string original = "function move() print('old') return false end";
        try
        {
            library.Save("probe", original, false);
            Assert.Throws<ArgumentException>(() => library.Save("probe", "return false", false));
            Assert.Throws<ArgumentException>(() => library.Patch("probe", "print('old')", "if"));
            Assert.Equal(original, library.Read("probe"));
            library.Patch("probe", "old", "new");
            Assert.Contains("new", library.Read("probe"));
            Assert.Equal(original, File.ReadAllText(Path.Combine(root, "probe.lua.bak")));
            Assert.Throws<ArgumentException>(() => library.Read("../other"));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
}
