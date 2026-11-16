using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Engine;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Personnage réellement observé (liste du compte sur un serveur, ou identité courante). Jamais inventé.</summary>
public sealed class ObservedCharacterChoice
{
    public ObservedCharacterChoice(string title, string detail, Func<Task> select)
    {
        Title = title;
        Detail = detail;
        Select = new AsyncRelayCommand(select);
    }

    public string Title { get; }
    public string Detail { get; }
    public ICommand Select { get; }
}

public partial class BotsViewModel
{
    private sealed record ObservedCharacter(int ServerId, long CharacterId, string Name, int? Level, string? Breed);

    private sealed class PendingCharacterList
    {
        public required string AccountId { get; init; }
        public required int Generation { get; init; }
        public required List<ObservedCharacter> Characters { get; init; }
    }

    // Abonné dès la construction : la liste des personnages arrive avant l'ouverture du menu.
    private int _sessionListsWatched;
    private readonly Dictionary<(string AccountId, int ServerId), List<ObservedCharacter>> _observedCharacters = new();
    private readonly Dictionary<int, PendingCharacterList> _pendingCharacterLists = new();
    private readonly Dictionary<string, Task<SessionTransitionResult>> _sessionRuns = new();

    [ObservableProperty] public partial bool EnhancedActivationBusy { get; set; }
    [ObservableProperty] public partial bool HasCharacterChoices { get; set; }
    [ObservableProperty] public partial string CharacterChoiceNotice { get; set; } = "";
    public ObservableCollection<ObservedCharacterChoice> CharacterChoices { get; } = new();

    private void EnsureSessionLists()
    {
        if (Interlocked.Exchange(ref _sessionListsWatched, 1) != 0) return;
        try
        {
            Stores.Launch.PacketDecoded += OnSessionListPacket;
            Stores.Launch.ServerIdentified += OnSessionServerKnown;
        }
        catch
        {
            Interlocked.Exchange(ref _sessionListsWatched, 0);
        }
    }

    private void OnSessionListPacket(DecodedPacket packet)
    {
        try
        {
            if (!string.Equals(packet.Name, "CharacterListEvent", StringComparison.Ordinal)) return;
            if (packet.Pid > int.MaxValue) return;
            var listed = new List<ObservedCharacter>();
            foreach (var (id, info) in CharacterInfoReader.ExtractAll(packet.Payload))
            {
                if (id <= 0 || string.IsNullOrWhiteSpace(info.Name)) continue;
                listed.Add(new ObservedCharacter(0, id, info.Name, info.Level, info.Breed));
            }
            if (listed.Count == 0) return;
            int pid = (int)packet.Pid;
            Dispatcher.UIThread.Post(() =>
            {
                try { RememberListed(pid, listed); }
                catch { /* liste déjà partie : le menu dira ce qui reste connu */ }
            });
        }
        catch
        {
            // Ne jamais casser les autres abonnés du flux de paquets.
        }
    }

    private void OnSessionServerKnown(uint pid, string _)
    {
        if (pid > int.MaxValue) return;
        int key = (int)pid;
        Dispatcher.UIThread.Post(() =>
        {
            try { FlushPending(key); }
            catch { /* le prochain affichage réessaie */ }
        });
    }

    private void RememberListed(int pid, List<ObservedCharacter> listed)
    {
        var row = _accounts.Accounts.FirstOrDefault(a => a.Pid == pid);
        if (row is null || string.IsNullOrEmpty(row.Model.Id)) return;
        int serverId = Stores.Launch.ServerIdOf((uint)pid);
        if (serverId > 0)
        {
            CommitObserved(row.Model.Id, serverId, listed);
            _pendingCharacterLists.Remove(pid);
            return;
        }
        _pendingCharacterLists[pid] = new PendingCharacterList
        {
            AccountId = row.Model.Id,
            Generation = row.LaunchGen,
            Characters = listed,
        };
    }

    private void FlushPending(int pid)
    {
        if (!_pendingCharacterLists.TryGetValue(pid, out var pending)) return;
        var row = _accounts.Accounts.FirstOrDefault(a => a.Pid == pid);
        if (row is null || row.Model.Id != pending.AccountId || row.LaunchGen != pending.Generation)
        {
            _pendingCharacterLists.Remove(pid);
            return;
        }
        int serverId = Stores.Launch.ServerIdOf((uint)pid);
        if (serverId <= 0) return;
        CommitObserved(pending.AccountId, serverId, pending.Characters);
        _pendingCharacterLists.Remove(pid);
    }

    private void CommitObserved(string accountId, int serverId, IReadOnlyList<ObservedCharacter> characters)
    {
        if (serverId <= 0 || string.IsNullOrEmpty(accountId)) return;
        var clean = new List<ObservedCharacter>();
        var seen = new HashSet<long>();
        foreach (var character in characters)
        {
            if (character.CharacterId <= 0 || string.IsNullOrWhiteSpace(character.Name) || !seen.Add(character.CharacterId)) continue;
            clean.Add(character with { ServerId = serverId });
        }
        if (clean.Count == 0) return;
        _observedCharacters[(accountId, serverId)] = clean;
    }

    private void PrepareObservations(AccountRow row)
    {
        EnsureSessionLists();
        if (row.Pid is int pid) FlushPending(pid);
    }

    private bool WasObserved(string accountId, SessionIdentity target)
    {
        if (!target.IsKnown) return false;
        if (_observedCharacters.TryGetValue((accountId, target.ServerId), out var list))
        {
            foreach (var character in list)
            {
                if (new SessionIdentity(target.ServerId, character.CharacterId, character.Name).Matches(target))
                    return true;
            }
        }
        var source = CaptureSessionSource(accountId);
        return source is not null && source.Identity.Matches(target);
    }

    internal void RebuildCharacterChoices()
    {
        Dispatcher.UIThread.VerifyAccess();
        CharacterChoices.Clear();
        HasCharacterChoices = false;
        var row = SelectedBot?.Row;
        if (row is null)
        {
            CharacterChoiceNotice = "Aucun personnage sélectionné.";
            return;
        }
        PrepareObservations(row);
        var source = CaptureSessionSource(row.Model.Id);
        if (source is null || !source.Identity.IsKnown)
        {
            CharacterChoiceNotice = "Aucun personnage complet n'a été observé pour cette session.";
            return;
        }
        int pid = row.Pid ?? 0;
        var others = new List<ObservedCharacter>();
        foreach (var pair in _observedCharacters)
        {
            if (!string.Equals(pair.Key.AccountId, row.Model.Id, StringComparison.Ordinal)) continue;
            foreach (var character in pair.Value)
            {
                if (character.ServerId == source.Identity.ServerId && character.CharacterId == source.Identity.CharacterId)
                    continue;
                if (!new SessionIdentity(character.ServerId, character.CharacterId, character.Name).IsKnown) continue;
                others.Add(character);
            }
        }
        if (others.Count == 0)
        {
            CharacterChoiceNotice = "Seul le personnage actuel est connu (" + source.Identity.Character
                + " · " + ServerLabel(source.Identity.ServerId, pid) + "). Aucun autre personnage ou serveur observé pour ce compte.";
            return;
        }
        others.Sort((a, b) =>
        {
            int server = string.Compare(ServerLabel(a.ServerId, pid), ServerLabel(b.ServerId, pid), StringComparison.OrdinalIgnoreCase);
            return server != 0 ? server : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
        var captured = source;
        foreach (var character in others)
        {
            var identity = new SessionIdentity(character.ServerId, character.CharacterId, character.Name);
            string detail = Describe(character, pid);
            CharacterChoices.Add(new ObservedCharacterChoice(character.Name, detail, () => SelectObservedAsync(captured, identity)));
        }
        CharacterChoiceNotice = "Personnage actuel : " + source.Identity.Character + " · " + ServerLabel(source.Identity.ServerId, pid);
        HasCharacterChoices = true;
    }

    private async Task SelectObservedAsync(SessionSource source, SessionIdentity target)
    {
        if (EnhancedActivationBusy || _sessionRuns.ContainsKey(source.AccountId)) return;
        await RunTransitionAsync(new SessionTransitionRequest(source, new SessionTarget(source.Mode, target))).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenGameWindowAsync()
    {
        if (SelectedBot?.Row is not { Running: true, IsSocketSession: true } row) return;
        if (row.SessionTransitionActive || EnhancedActivationBusy || row.ReconnectPending) return;
        var source = CaptureSessionSource(row.Model.Id);
        if (source is null || source.Mode != ConnectionMode.Socket || !source.Identity.IsKnown)
        {
            Toasts.Show("Le personnage et le serveur de cette session ne sont pas encore connus.", success: false);
            return;
        }
        await RunTransitionAsync(new SessionTransitionRequest(source, new SessionTarget(ConnectionMode.Mitm, source.Identity))).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelSessionChange()
    {
        if (SelectedBot?.Row.Model.Id is not { Length: > 0 } accountId) return;
        CancelSessionTransition(accountId);
    }

    [RelayCommand]
    private async Task ActivateEnhancedAsync()
    {
        // L'énergie du personnage n'entre pas dans cette décision.
        if (SelectedBot?.Row is not { Running: true, IsSocketSession: false, Pid: int pid } || pid <= 0)
        {
            Toasts.Show("Sélectionne un client MITM en jeu.", success: false);
            return;
        }
        if (SelectedBot.Row.SessionTransitionActive || EnhancedActivationBusy || SelectedBot.Row.ReconnectPending) return;
        await ActivateEnhancedCoreAsync(pid).ConfigureAwait(true);
    }

    private async Task<SessionTransitionResult> RunTransitionAsync(SessionTransitionRequest request)
    {
        Dispatcher.UIThread.VerifyAccess();
        string accountId = request.Source.AccountId;
        if (_sessionRuns.ContainsKey(accountId))
        {
            var busy = Rejected("Account is already reconnecting.");
            ToastTransition(busy);
            return busy;
        }
        var done = new TaskCompletionSource<SessionTransitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessionRuns[accountId] = done.Task;
        try
        {
            var result = await TransitionSessionAsync(request).ConfigureAwait(true);
            done.TrySetResult(result);
            ToastTransition(result);
            return result;
        }
        catch (Exception e)
        {
            var failed = new SessionTransitionResult(SessionTransitionState.Rejected, false, e.Message);
            done.TrySetResult(failed);
            ToastTransition(failed);
            return failed;
        }
        finally
        {
            if (_sessionRuns.TryGetValue(accountId, out var current) && ReferenceEquals(current, done.Task))
                _sessionRuns.Remove(accountId);
        }
    }

    private static SessionTransitionResult Rejected(string message)
        => new(SessionTransitionState.Rejected, false, message);

    private async Task<(bool Ok, string Message)> ActivateEnhancedCoreAsync(int pid)
    {
        if (EnhancedActivationBusy) return (false, "Enhanced activation is already running.");
        EnhancedActivationBusy = true;
        try
        {
            string message = await DofusEnhanced.ActivateAsync(pid).ConfigureAwait(true);
            Toasts.Show(message);
            return (true, message);
        }
        catch (Exception e)
        {
            string message = string.IsNullOrWhiteSpace(e.Message) ? "Activation Enhanced interrompue." : e.Message;
            Toasts.Show(message, success: false);
            return (false, message);
        }
        finally { EnhancedActivationBusy = false; }
    }

    private string ServerLabel(int serverId, int pid)
    {
        string? name = null;
        try { name = Stores.Launch.GameNames.ServerName(serverId); }
        catch { /* tables de noms absentes : on affiche l'id observé */ }
        if (string.IsNullOrWhiteSpace(name) && pid > 0 && Stores.Launch.ServerIdOf((uint)pid) == serverId)
        {
            string current = Stores.Launch.ServerNameOf((uint)pid);
            if (!string.IsNullOrWhiteSpace(current)) name = current;
        }
        return string.IsNullOrWhiteSpace(name) ? "serveur " + serverId.ToString(CultureInfo.InvariantCulture) : name;
    }

    private string Describe(ObservedCharacter character, int pid)
    {
        var parts = new List<string>();
        if (character.Level is int level and > 0) parts.Add("Niveau " + level.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(character.Breed)) parts.Add(character.Breed);
        parts.Add(ServerLabel(character.ServerId, pid));
        return string.Join(" · ", parts);
    }

    private static void ToastTransition(SessionTransitionResult result)
    {
        string text = FrenchSessionMessage(result.Message);
        if (text.Length == 0) text = FrenchSessionState(result.State);
        if (text.Length == 0) return;
        Toasts.Show(text, success: result.Ok, sound: false);
    }

    internal static string FrenchSessionState(SessionTransitionState state) => state switch
    {
        SessionTransitionState.Preflight => "Vérification de la session…",
        SessionTransitionState.Closing => "Fermeture de la session…",
        SessionTransitionState.Launching => "Connexion du personnage choisi…",
        SessionTransitionState.Confirming => "Confirmation du personnage en jeu…",
        SessionTransitionState.Completed => "Personnage confirmé en jeu.",
        SessionTransitionState.Rejected => "Changement refusé.",
        SessionTransitionState.Cancelled => "Changement annulé.",
        SessionTransitionState.Superseded => "La session a changé avant la fin.",
        SessionTransitionState.Disconnected => "Session déconnectée.",
        _ => "",
    };

    internal static string FrenchSessionMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        return SessionMessages.TryGetValue(message, out var french) ? french : message;
    }

    private static readonly Dictionary<string, string> SessionMessages = new(StringComparer.Ordinal)
    {
        ["Account no longer exists."] = "Ce compte n'est plus là.",
        ["Account is already reconnecting."] = "Une reconnexion est déjà en cours.",
        ["An exact observed server and character identity is required."] = "Il faut un serveur et un personnage exactement observés.",
        ["Targeted MITM server switching is not supported; select Socket for an explicit server."] = "Le client MITM ne change pas de serveur. Choisis Socket pour un serveur précis.",
        ["License is not authorized."] = "La licence n'est pas autorisée.",
        ["Source session is busy or not in game."] = "La session est occupée ou pas en jeu.",
        ["Source Socket completion is unavailable."] = "La fin de la session Socket n'est pas disponible.",
        ["Socket permission is required."] = "Le droit Socket est nécessaire.",
        ["Dofus executable selection cancelled."] = "Choix de l'exécutable Dofus annulé.",
        ["Source session changed."] = "La session source a changé.",
        ["License or protocol mapping is unavailable."] = "Licence ou mapping du protocole indisponible.",
        ["Socket permission was revoked."] = "Le droit Socket a été retiré.",
        ["Launch route or multi-account permission refused."] = "Route de lancement ou droit multi-compte refusé.",
        ["Source became busy during preflight."] = "La session est devenue occupée pendant la vérification.",
        ["Transition already started."] = "Un changement est déjà en cours.",
        ["Source session changed during preflight."] = "La session source a changé pendant la vérification.",
        ["Source session ended before commit."] = "La session s'est terminée avant le changement.",
        ["Session ownership changed while closing."] = "La session a changé de propriétaire pendant la fermeture.",
        ["Target launch failed."] = "L'ouverture de la nouvelle session a échoué.",
        ["Session ownership changed while launching."] = "La session a changé de propriétaire pendant la connexion.",
        ["Session ownership changed before confirmation."] = "La session a changé avant la confirmation.",
        ["Target identity confirmed in game."] = "Personnage confirmé en jeu.",
        ["Target identity did not match."] = "Le personnage en jeu ne correspond pas à celui demandé.",
        ["Transition cancelled; session disconnected."] = "Changement annulé. La session a été déconnectée.",
        ["Transition cancelled; source preserved."] = "Changement annulé. La session d'origine est intacte.",
        ["This server and character were not observed for this account."] = "Ce serveur et ce personnage n'ont pas été observés pour ce compte.",
        ["Source session is not in game."] = "Aucune session en jeu pour ce compte.",
        ["No session transition is running."] = "Aucun changement de session en cours.",
        ["Cancel signaled; the transition result was not observed on this call."] = "Annulation demandée. Le résultat du changement n'a pas été observé.",
        ["Enhanced activation is already running."] = "Une activation Enhanced est déjà en cours.",
        ["Select a MITM client owned by this bot."] = "Sélectionne un client MITM appartenant à ce bot.",
        ["Account is already busy."] = "Le compte est déjà occupé.",
    };

    private async Task<JsonNode> ControlSessionTransitionAsync(JsonElement args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();
        var target = ControlWriteTarget(args);
        string modeText = Required(args, "mode");
        ConnectionMode mode = modeText.ToLowerInvariant() switch
        {
            "socket" => ConnectionMode.Socket,
            "mitm" => ConnectionMode.Mitm,
            _ => throw new ArgumentException("mode must be socket or mitm."),
        };
        int serverId = RequiredPositiveInt(args, "serverId");
        long characterId = RequiredPositiveLong(args, "characterId");
        string character = Required(args, "character");
        bool resume = OptionalBool(args, "resumeScript", true);
        var row = _accounts.Accounts.FirstOrDefault(r => r.Model.Id == target.AccountId);
        if (row is null) return FailTransition(Rejected("Account no longer exists."));
        PrepareObservations(row);
        var source = CaptureSessionSource(row.Model.Id);
        if (source is null) return FailTransition(Rejected("Source session is not in game."));
        var identity = new SessionIdentity(serverId, characterId, character);
        if (!WasObserved(row.Model.Id, identity))
            return FailTransition(Rejected("This server and character were not observed for this account."));
        var request = new SessionTransitionRequest(source, new SessionTarget(mode, identity), resume);
        return TransitionJson(await RunTransitionAsync(request).ConfigureAwait(true), request);
    }

    private async Task<JsonNode> ControlSessionTransitionCancelAsync(JsonElement args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();
        var target = ControlWriteTarget(args);
        if (!_sessionRuns.TryGetValue(target.AccountId, out var running))
        {
            var row = _accounts.Accounts.FirstOrDefault(r => r.Model.Id == target.AccountId);
            if (row?.SessionTransitionActive == true)
            {
                CancelSessionTransition(target.AccountId);
                return FailTransition(Rejected("Cancel signaled; the transition result was not observed on this call."));
            }
            return FailTransition(Rejected("No session transition is running."));
        }
        CancelSessionTransition(target.AccountId);
        try { return TransitionJson(await running.ConfigureAwait(true)); }
        catch (Exception e) { return TransitionJson(Rejected(e.Message)); }
    }

    private async Task<JsonNode> ControlEnhancedActivateAsync(JsonElement args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();
        var target = ControlWriteTarget(args);
        var row = _accounts.Accounts.FirstOrDefault(r => r.Model.Id == target.AccountId);
        if (row is null || !row.Running || row.IsSocketSession || row.Pid is not int pid || pid <= 0 || pid != target.Pid)
            return FailEnhanced("Select a MITM client owned by this bot.");
        if (row.SessionTransitionActive || row.ReconnectPending)
            return FailEnhanced("Account is already busy.");
        var outcome = await ActivateEnhancedCoreAsync(pid).ConfigureAwait(true);
        if (!outcome.Ok && outcome.Message == "Enhanced activation is already running.")
            Toasts.Show(FrenchSessionMessage(outcome.Message), success: false, sound: false);
        return EnhancedJson(outcome.Ok, outcome.Message);
    }

    private static JsonObject FailTransition(SessionTransitionResult result)
    {
        ToastTransition(result);
        return TransitionJson(result);
    }

    private static JsonObject TransitionJson(SessionTransitionResult result, SessionTransitionRequest? request = null)
    {
        var json = new JsonObject
        {
            ["ok"] = result.Ok,
            ["state"] = result.State.ToString(),
            ["committed"] = result.Committed,
            ["message"] = result.Message,
            ["completionConfirmed"] = result.Ok,
        };
        if (request is null) return json;
        json["mode"] = request.Target.Mode == ConnectionMode.Socket ? "socket" : "mitm";
        json["serverId"] = request.Target.Identity.ServerId;
        json["characterId"] = request.Target.Identity.CharacterId;
        json["character"] = request.Target.Identity.Character;
        return json;
    }

    private static JsonObject FailEnhanced(string message)
    {
        Toasts.Show(FrenchSessionMessage(message), success: false, sound: false);
        return EnhancedJson(false, message);
    }

    private static JsonObject EnhancedJson(bool ok, string message) => new()
    {
        ["ok"] = ok,
        ["message"] = message,
        ["completionConfirmed"] = ok,
    };

    private static int RequiredPositiveInt(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number <= 0)
            throw new ArgumentException($"'{key}' must be a positive integer.");
        return number;
    }

    private static long RequiredPositiveLong(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long number) || number <= 0)
            throw new ArgumentException($"'{key}' must be a positive integer.");
        return number;
    }

    private static bool OptionalBool(JsonElement args, string key, bool fallback)
    {
        if (!args.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException($"'{key}' must be a boolean.");
        return value.GetBoolean();
    }
}
