using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.Services.Supervisor;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel : ITeamSupervisorPort
{
    public async Task<JsonNode> ControlTeamAsync(string action, JsonElement args, CancellationToken ct)
    {
        static JsonNode Json(object value) => JsonSerializer.SerializeToNode(value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        ct.ThrowIfCancellationRequested();
        if (args.ValueKind != JsonValueKind.Object) return Json(TeamControlResult.Error("args must be an object"));
        var copy = args.Clone();
        if (action == "waitGathered")
        {
            int ms = 10000;
            if (copy.TryGetProperty("timeout", out var value) && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out ms)))
                return Json(TeamControlResult.Error("timeout must be an integer in milliseconds"));
            return Json(await TeamSupervisorWait.GatherAsync(token => TeamOnUiAsync("observeGathered", copy, token), ms, ct));
        }
        return Json(await TeamOnUiAsync(action, copy, ct));
    }

    private async Task<TeamControlResult> TeamOnUiAsync(string action, JsonElement args, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        var done = new TaskCompletionSource<TeamControlResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = bounded.Token.Register(() => done.TrySetCanceled(bounded.Token));
        Dispatcher.UIThread.Post(() =>
        {
            if (bounded.IsCancellationRequested) return;
            try { done.TrySetResult(ControlTeamOnUi(action, args)); }
            catch (Exception ex) { done.TrySetException(ex); }
        });
        return await done.Task.ConfigureAwait(false);
    }

    private static object SupervisorTeamSnapshot(Team t) => new
    {
        id = t.Id, name = t.Name, formed = t.Active, connecting = t.Connecting,
        follow = t.FollowEnabled, combatJoin = t.CombatJoinEnabled,
        members = t.Roster.Select(m => new { accountId = m.AccountId, name = m.Name, server = m.Server, leader = m.Leader }).ToArray()
    };

    private TeamControlResult ControlTeamOnUi(string action, JsonElement args)
    {
        string Text(string key) => args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (!LicenseService.IsAuthorized || !LicenseService.HasPermission(LicenseService.Perm.Multi))
            return TeamControlResult.Error("permission Multi required");
        if (action == "list") return new(true, "observed", Data: Teams.Select(SupervisorTeamSnapshot).ToArray());
        if (action == "create")
        {
            var name = Text("name").Trim();
            if (name.Length == 0) return TeamControlResult.Error("name is required");
            if (!args.TryGetProperty("members", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0)
                return TeamControlResult.Error("nonempty members array required; members must be in game");
            var selected = new System.Collections.Generic.List<BotRowViewModel>();
            foreach (var id in ids.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String) return TeamControlResult.Error("members must contain account IDs");
                var bot = Bots.FirstOrDefault(b => b.Row.Model.Id == id.GetString());
                if (bot is null || selected.Contains(bot) || string.IsNullOrWhiteSpace(bot.Row.CharacterName))
                    return TeamControlResult.Error("unknown, offline or duplicate account");
                if (Teams.Any(t => t.Roster.Any(m => m.AccountId == bot.Row.Model.Id || RefMatches(m, bot))))
                    return TeamControlResult.Error("account already belongs to a team");
                if (string.IsNullOrWhiteSpace(bot.Row.Server) || bot.Row.Server == "-" || selected.Any(b => !SameServer(b, bot)))
                    return TeamControlResult.Error("all members must have the same resolved server");
                if (selected.Any(b => string.Equals(b.Row.CharacterName, bot.Row.CharacterName, StringComparison.OrdinalIgnoreCase)))
                    return TeamControlResult.Error("duplicate character identity");
                selected.Add(bot);
            }
            var leaderId = Text("leader");
            if (leaderId.Length > 0 && selected.All(b => b.Row.Model.Id != leaderId))
                return TeamControlResult.Error("leader must be a member account ID");
            AddTeam();
            var created = Teams.Last();
            created.Name = name;
            foreach (var bot in selected) AssignBot(created, bot);
            if (leaderId.Length > 0)
            {
                foreach (var m in created.Roster) m.Leader = m.AccountId == leaderId;
                ReconcileTeams(); MarkTeamsDirty();
            }
            return new(true, "completed", Data: SupervisorTeamSnapshot(created));
        }
        var team = Teams.FirstOrDefault(t => t.Id == Text("teamId"));
        if (team is null)
        {
            var named = Teams.Where(t => t.Name == Text("name")).ToArray();
            if (named.Length == 1) team = named[0];
        }
        if (team is null) return TeamControlResult.Error("unknown or ambiguous team; provide teamId or unique name");
        switch (action)
        {
            case "get": return new(true, "observed", Data: SupervisorTeamSnapshot(team));
            case "delete":
                if (team.Active || team.Connecting) return TeamControlResult.Error("formed or connecting membership is frozen");
                RemoveTeam(team); return new(true, "completed");
            case "setLeader":
                if (team.Active || team.Connecting) return TeamControlResult.Error("formed or connecting membership is frozen");
                var member = team.Roster.FirstOrDefault(m => m.AccountId == Text("leader"));
                if (member is null) return TeamControlResult.Error("account is not a member");
                foreach (var m in team.Roster) m.Leader = ReferenceEquals(m, member);
                ReconcileTeams(); MarkTeamsDirty(); break;
            case "setFollow": case "setCombatJoin":
                if (!args.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return TeamControlResult.Error("enabled must be boolean");
                if (action == "setFollow") team.FollowEnabled = enabled.GetBoolean();
                else team.CombatJoinEnabled = enabled.GetBoolean();
                break;
            case "form":
                if (team.Active || team.Connecting || !team.CanForm || team.Members.Count != team.Roster.Count)
                    return TeamControlResult.Error("team must be idle with every member and leader in game");
                if (team.Leader is not { } leader || team.Members.Any(m => !SameServer(leader, m)))
                    return TeamControlResult.Error("all members must have the same resolved server");
                FormTeam(team);
                return new(true, "requested", "local formation applied; party invitations are not acknowledged", SupervisorTeamSnapshot(team));
            case "disband":
                if (team.Connecting) return TeamControlResult.Error("team connection is still in progress");
                DisbandTeam(team); break;
            case "start":
                if (Text("script").Length > 0) return TeamControlResult.Error("script override unsupported; team start uses the leader planning script");
                if (!team.Active) return TeamControlResult.Error("team must be formed");
                StartTeam(team);
                if (team.Leader is not { } lead || !_luaHosts.TryGetValue(lead.Pid, out var host) || !host.Running)
                    return TeamControlResult.Error("leader script is not observed running; start was refused or ended");
                return new(true, "running", "leader host observed running; no game outcome implied");
            case "stop":
                StopTeam(team);
                if (team.Members.Any(m => _luaHosts.TryGetValue(m.Pid, out var h) && h.Running))
                    return new(true, "requested", "stop requested; script termination pending");
                break;
            case "connect": return TeamControlResult.Error("unsupported: existing team launcher has no cancellable result contract");
            case "observeGathered":
                UpdateTeam();
                bool gathered = team.Active && team.Leader is { MapId: > 0 } && team.Roster.Count >= 2 && team.Members.Count == team.Roster.Count
                    && team.Members.All(m => m.TeamPresence == TeamPresenceKind.SameMap);
                long party = team.Leader is { } partyLeader ? Stores.Launch.PartyIdOf((uint)partyLeader.Pid) : 0;
                gathered = gathered && party > 0 && team.Members.All(m => Stores.Launch.PartyIdOf((uint)m.Pid) == party
                    && team.Members.All(other => Stores.Launch.IsInParty((uint)m.Pid, (uint)other.Pid, other.Row.CharacterName ?? "")));
                return new(true, gathered ? "gathered" : "pending", "requires matching map and server-observed party membership for every member");
            default: return TeamControlResult.Error("unsupported team action");
        }
        return new(true, "completed", Data: SupervisorTeamSnapshot(team));
    }
}
