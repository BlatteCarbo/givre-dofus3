using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.ViewModels;
using GivreClone.App.Services.WorldAtlasMcp;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ItemCraftMcp;
using GivreControl;

namespace GivreClone.App.Services.Mcp;

public sealed class LocalControlBridge
{
    private readonly BotsViewModel _bots;
    private readonly ControlPipeServer _server;
    public LocalControlBridge(BotsViewModel bots)
    {
        _bots = bots;
        _server = new ControlPipeServer(ControlPipeClient.PipeName, HandleAsync);
    }
    public void Start() => _server.Start();
    public void Stop() => _server.Stop();

    private Task<JsonNode> HandleAsync(ControlRequest request, CancellationToken ct) => HandleAsync(request, ct, remote: false);

    internal async Task<JsonNode> HandleAsync(ControlRequest request, CancellationToken ct, bool remote)
    {
        if (!remote && !AppConfig.Current.McpEnabled) throw new InvalidOperationException("Local MCP is disabled in GivreWork settings.");
        if (request.Tool == "query_item_craft")
        {
            var args = request.Arguments;
            string? query = args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty("requestJson", out var payload) &&
                payload.ValueKind == JsonValueKind.String ? payload.GetString() : null;
            var source = ItemCraftAccessService.ApprovedMcpSnapshot();
            var result = ItemCraftMcpFacade.QueryJson(query, source);
            return JsonNode.Parse(result) ?? new JsonObject { ["status"] = "UNKNOWN" };
        }
        if (request.Tool == "query_profession_plan")
        {
            var args = request.Arguments;
            string? query = args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty("requestJson", out var payload) &&
                payload.ValueKind == JsonValueKind.String ? payload.GetString() : null;
            var snapshot = ProfessionAccessService.ApprovedMcpSnapshotFromQuery(query);
            var answer = ProfessionMcpFacade.QueryJson(query, snapshot);
            return JsonNode.Parse(answer) ?? new JsonObject { ["status"] = "UNKNOWN" };
        }
        if (request.Tool == "query_world_atlas")
        {
            var args = request.Arguments;
            string? query = args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty("requestJson", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
            var snapshot = AtlasAccessService.McpSnapshot();
            var answer = WorldAtlasMcpFacade.QueryJson(query, snapshot);
            return JsonNode.Parse(answer) ?? new JsonObject { ["status"] = "UNKNOWN" };
        }
        if (request.Tool == "wait_console")
        {
            var journal = await Dispatcher.UIThread.InvokeAsync(() => _bots.ControlJournal(request.Arguments), DispatcherPriority.Normal, ct);
            var args = request.Arguments;
            var page = await journal.WaitAsync(args.TryGetProperty("since", out var since) ? since.GetInt64() : 0,
                args.GetProperty("pattern").GetString() ?? "", args.TryGetProperty("timeout", out var timeout) ? timeout.GetInt32() : 30, ct);
            return ControlJson.Console(page);
        }
        var inner = await Dispatcher.UIThread.InvokeAsync(() => _bots.ControlAsync(request, ct), DispatcherPriority.Normal, ct);
        return await inner;
    }
}

internal static class ControlJson
{
    public static JsonObject Console(ConsolePage page)
    {
        var rows = new JsonArray();
        foreach (var line in page.Lines) rows.Add(new JsonObject { ["cursor"] = line.Cursor, ["time"] = line.Time.ToString("O"),
            ["level"] = line.Level, ["category"] = line.Category, ["message"] = line.Message });
        return new JsonObject { ["lines"] = rows, ["cursor"] = page.Cursor, ["truncated"] = page.Truncated };
    }
}
