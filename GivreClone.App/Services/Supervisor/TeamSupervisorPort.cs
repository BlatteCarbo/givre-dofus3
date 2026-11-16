using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Supervisor;

public interface ITeamSupervisorPort
{
    Task<JsonNode> ControlTeamAsync(string action, JsonElement args, CancellationToken ct);
}

public sealed record TeamControlResult(bool Ok, string Status, string Reason = "", object? Data = null)
{
    public static TeamControlResult Error(string reason) => new(false, "error", reason);
}

public static class TeamSupervisorWait
{
    public const int MaxTimeoutMs = 120000;

    public static async Task<object> GatherAsync(Func<CancellationToken, Task<TeamControlResult>> observe,
        int timeoutMs, CancellationToken ct)
    {
        if (timeoutMs < 0 || timeoutMs > MaxTimeoutMs) return TeamControlResult.Error("timeoutMs must be between 0 and 120000");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Math.Max(1, timeoutMs));
        try
        {
            do
            {
                ct.ThrowIfCancellationRequested();
                var result = await observe(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                if (!result.Ok || result.Status == "gathered") return result;
                if (timeoutMs == 0) break;
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            } while (true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        ct.ThrowIfCancellationRequested();
        return new TeamControlResult(false, "timeout", "gathering was not observed before the deadline");
    }
}
