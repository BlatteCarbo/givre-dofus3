using System;
using System.Text.Json;
using System.Threading;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.Supervisor;

public sealed partial class SupervisorLuaHost
{
    private void BindTeams(Script s, Module module, CancellationToken ct)
    {
        foreach (var action in new[] { "list", "get", "create", "form", "disband", "delete", "setLeader", "setFollow", "setCombatJoin", "start", "stop", "connect", "waitGathered" })
        {
            Verb(module, action, args =>
            {
                if (!_svc.Perms.HasMulti) return TeamLuaResult(s, TeamControlResult.Error(NeedMulti));
                if (_svc.Teams is null) return TeamLuaResult(s, TeamControlResult.Error("teams port unavailable"));
                var input = Arg(args, 0);
                if (input.Type is not (DataType.Nil or DataType.Table))
                    return TeamLuaResult(s, TeamControlResult.Error("expected an options table"));
                JsonElement options;
                try { options = JsonSerializer.SerializeToElement(input.Type == DataType.Nil ? new object() : LuaData.ToObject(input)); }
                catch (ArgumentException ex) { return TeamLuaResult(s, TeamControlResult.Error(ex.Message)); }
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(action == "waitGathered" ? TeamSupervisorWait.MaxTimeoutMs + 1000 : DefaultOrderTimeoutMs);
                var task = _svc.Teams.ControlTeamAsync(action, options, deadline.Token);
                try
                {
                    while (!task.IsCompleted) { deadline.Token.ThrowIfCancellationRequested(); PumpingDelay(20, ct); }
                    return TeamLuaResult(s, task.GetAwaiter().GetResult());
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { return TeamLuaResult(s, new TeamControlResult(false, "timeout", "team operation deadline exceeded; outcome unconfirmed")); }
            });
        }
    }

    private static DynValue TeamLuaResult(Script script, object result)
        => TeamJsonToLua(script, JsonSerializer.SerializeToElement(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    private static DynValue TeamJsonToLua(Script script, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var table = new Table(script);
                foreach (var entry in value.EnumerateObject()) table[entry.Name] = TeamJsonToLua(script, entry.Value);
                return DynValue.NewTable(table);
            case JsonValueKind.Array:
                var array = new Table(script);
                int index = 1;
                foreach (var entry in value.EnumerateArray()) array[index++] = TeamJsonToLua(script, entry);
                return DynValue.NewTable(array);
            case JsonValueKind.String: return DynValue.NewString(value.GetString()!);
            case JsonValueKind.Number: return DynValue.NewNumber(value.GetDouble());
            case JsonValueKind.True: return DynValue.True;
            case JsonValueKind.False: return DynValue.False;
            default: return DynValue.Nil;
        }
    }
}
