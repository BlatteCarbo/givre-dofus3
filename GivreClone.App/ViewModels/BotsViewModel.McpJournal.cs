using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.Services.Mcp;
using GivreClone.App.Services;
using GivreControl;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    private readonly McpActionJournal _mcpActions = new();
    public ObservableCollection<McpActionEntry> McpActions { get; } = [];

    private async Task PublishMcpActionsAsync() => await Dispatcher.UIThread.InvokeAsync(() =>
    {
        McpActions.Clear();
        foreach (var row in _mcpActions.Read()) McpActions.Add(row);
    });

    internal async Task<JsonNode> ControlAsync(ControlRequest request, CancellationToken ct)
    {
        int pid = 0;
        string target = "Application";
        if (request.Arguments.ValueKind == JsonValueKind.Object && request.Arguments.TryGetProperty("bot", out _))
        {
            target = "Cible non résolue";
            try
            {
                var identity = ControlIdentity(request.Arguments);
                pid = identity.Target.Pid;
                target = string.IsNullOrWhiteSpace(identity.Target.Character)
                    ? identity.Target.AccountId : identity.Target.Character;
            }
            catch (ArgumentException) { /* the core returns the actual target error */ }
            catch (InvalidOperationException) { /* directory not ready, still journal the call */ }
        }
        long id = _mcpActions.Begin(JournalToolName(request.Tool), target);
        await PublishMcpActionsAsync();
        try
        {
            var result = await ControlCoreAsync(request, ct);
            var response = result as JsonObject;
            McpActionState state;
            if (IsSessionControl(request.Tool)) state = SessionControlJournalState(response);
            else
            {
                string? status = response?["status"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
                bool refused = response?["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var success) && !success;
                state = refused || status == "not-started" ? McpActionState.Refused
                    : status is "accepted" or "starting" or "running" ? McpActionState.Accepted : McpActionState.Returned;
            }
            _mcpActions.SetState(id, state);
            await PublishMcpActionsAsync();
            if (state == McpActionState.Accepted && pid > 0)
                TrackMcpExecution(id, pid, request.Tool);
            return result;
        }
        catch (OperationCanceledException)
        {
            _mcpActions.SetState(id, McpActionState.Cancelled);
            await PublishMcpActionsAsync();
            throw;
        }
        catch
        {
            _mcpActions.SetState(id, McpActionState.Failed);
            await PublishMcpActionsAsync();
            throw;
        }
    }

    // Le journal n'accepte pas le point dans le nom d'outil. L'appel réel garde session.transition / enhanced.activate.
    private static string JournalToolName(string tool) => tool switch
    {
        "session.transition" => "session_transition",
        "session.transition.cancel" => "session_transition_cancel",
        "enhanced.activate" => "enhanced_activate",
        _ => tool,
    };

    private static bool IsSessionControl(string tool)
        => tool is "session.transition" or "session.transition.cancel" or "enhanced.activate";

    // Jamais « accepté » : ok n'est vrai qu'une fois le résultat réel confirmé.
    private static McpActionState SessionControlJournalState(JsonObject? response)
    {
        bool ok = response?["ok"] is JsonValue okValue && okValue.TryGetValue<bool>(out var passed) && passed;
        bool confirmed = response?["completionConfirmed"] is JsonValue done && done.TryGetValue<bool>(out var finished) && finished;
        string? transition = response?["state"] is JsonValue state && state.TryGetValue<string>(out var name) ? name : null;
        if (ok && confirmed) return McpActionState.Returned;
        if (string.Equals(transition, "Cancelled", StringComparison.Ordinal)) return McpActionState.Cancelled;
        return McpActionState.Refused;
    }

    private void TrackMcpExecution(long id, int pid, string tool)
    {
        void End()
        {
            _mcpActions.SetState(id, McpActionState.ExecutionEnded);
            Dispatcher.UIThread.Post(() => _ = PublishMcpActionsAsync());
        }
        if (tool is "run_lua_script" or "run_saved_script" or "travel_to")
        {
            if (!_luaHosts.TryGetValue(pid, out var host)) return;
            async Task FinishedAsync(Task<LuaRunOutcome> completion)
            {
                McpActionState state;
                try
                {
                    state = (await completion.ConfigureAwait(false)) switch
                    {
                        LuaRunOutcome.Completed => McpActionState.ExecutionCompleted,
                        LuaRunOutcome.Stopped => McpActionState.ExecutionEnded,
                        _ => McpActionState.ExecutionFailed,
                    };
                }
                catch (OperationCanceledException) { state = McpActionState.ExecutionEnded; }
                catch { state = McpActionState.ExecutionFailed; }
                _mcpActions.SetState(id, state);
                Dispatcher.UIThread.Post(() => _ = PublishMcpActionsAsync());
            }
            _ = FinishedAsync(host.RunCompletion);
        }
        else if (tool == "start_smith_leveling")
        {
            async Task FinishedAsync()
            {
                try
                {
                    await (SmithMagic.StartCommand.ExecutionTask ?? Task.CompletedTask);
                    End();
                }
                catch
                {
                    _mcpActions.SetState(id, McpActionState.Failed);
                    await PublishMcpActionsAsync();
                }
            }
            _ = FinishedAsync();
        }
    }
}
