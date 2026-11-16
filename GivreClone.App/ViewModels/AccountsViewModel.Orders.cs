using System;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;
using GivreClone.App.Services.Supervisor;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Protocol;
using GivreClone.Engine.Accounts;

namespace GivreClone.App.ViewModels;

// Ordres des superviseurs (bots:connect / disconnect / reconnect) : thread UI uniquement. Réutilise les chemins existants et leurs gardes.
public partial class AccountsViewModel
{
    /// <summary>Un modérateur a contacté ce perso il y a moins de 2 h : bots:reconnect est refusé (portail).</summary>
    public static readonly TimeSpan ModeratorReconnectBan = TimeSpan.FromHours(2);

    internal AccountRow? RowByModelId(string id) => Accounts.FirstOrDefault(r => r.Model.Id == id);

    internal async Task<OrderResult> OrderConnectAsync(AccountRow row, string? character, string? server)
    {
        if (!LicenseService.IsAuthorized) return OrderResult.No("session non autorisée");
        if (LicenseService.IsSuspended) return OrderResult.No("reprise de session bloquée");
        if (!string.IsNullOrWhiteSpace(server) && row.Model.Mode != ConnectionMode.Socket)
            return OrderResult.No("le choix explicite du serveur demande le mode Socket");
        if (row.Running)
            return string.IsNullOrWhiteSpace(character) || string.Equals(row.CharacterName, character, StringComparison.OrdinalIgnoreCase)
                ? OrderResult.Yes
                : OrderResult.No("déjà en jeu sur un autre perso (bots:switchCharacter)");
        bool ok = await LaunchWithServerPickAsync(row.Login, character, server,
            () => LaunchInGameForTeamAsync(row, string.IsNullOrWhiteSpace(character) ? null : character));
        return ok ? OrderResult.Yes : OrderResult.No(string.IsNullOrWhiteSpace(row.Status) ? "lancement refusé" : row.Status);
    }

    internal static async Task<bool> LaunchWithServerPickAsync(string login, string? character, string? server, Func<Task<bool>> launch)
    {
        bool scriptedPick = !string.IsNullOrWhiteSpace(server);
        if (scriptedPick) SocketScriptedPickRegistry.Set(login, new ScriptedPick(server!.Trim(), string.IsNullOrWhiteSpace(character) ? null : character.Trim()));
        bool ok = false;
        try { return ok = await launch(); }
        finally { if (scriptedPick && !ok) SocketScriptedPickRegistry.Clear(login); }
    }

    /// <summary>Création scriptée : le serveur est imposé (aucune fenêtre), la session crée le personnage puis le sélectionne. Mode Socket requis.</summary>
    internal async Task<OrderResult> OrderCreateCharacterAsync(AccountRow row, CharacterSpec spec)
    {
        if (row.Running) return OrderResult.No("déconnecte d'abord le compte");
        if (row.Model.Mode != ConnectionMode.Socket) return OrderResult.No("la création de personnage demande le mode Socket");
        if (string.IsNullOrWhiteSpace(spec.Server)) return OrderResult.No("serveur requis (nom ou numéro)");
        if (_launch.GameData is not { } db) return OrderResult.No("données du jeu indisponibles (dofus.sqlite)");
        int breedIndex = -1;
        for (int i = 0; CharacterInfoReader.BreedName(i) is { } bn; i++)
            if (string.Equals(bn, spec.Breed?.Trim(), StringComparison.OrdinalIgnoreCase) || (int.TryParse(spec.Breed, out var n) && n == ClassIconProvider.BreedId(i))) { breedIndex = i; break; }
        if (breedIndex < 0) return OrderResult.No("classe inconnue (nom français ou numéro)");
        int gender = (spec.Sex ?? "").Trim().ToLowerInvariant() switch
        {
            "m" or "male" or "masculin" or "homme" or "0" => 0,
            "f" or "female" or "feminin" or "féminin" or "femme" or "1" => 1,
            _ => -1,
        };
        if (gender < 0) return OrderResult.No("sexe inconnu (m | f)");
        string name = (spec.Name ?? "").Trim();
        if (name.Length > 0 && AccountsLogic.ValidateName(name) is { } nameErr) return OrderResult.No(nameErr);
        int breedId = ClassIconProvider.BreedId(breedIndex);
        var heads = db.CreationHeads(breedId, gender); var bodies = db.CreationBodies(breedId, gender);
        if (heads.Count == 0 || bodies.Count == 0) return OrderResult.No("têtes ou corps de cette classe introuvables dans les données du jeu");
        int Pick(IReadOnlyList<CreationPart> l, int? rank) => rank is >= 1 && rank <= l.Count ? l[rank.Value - 1].Id : l[Random.Shared.Next(l.Count)].Id;
        int head = Pick(heads, spec.Head), body = Pick(bodies, spec.Body);
        var colors = db.BreedDefaultColors(breedId, gender).ToArray();
        if (colors.Length != CharacterCreationEncoder.ColorCount) colors = new int[CharacterCreationEncoder.ColorCount];
        string[] keys = { "skin", "hair", "outfit1", "outfit2", "outfit3", "outfit4" };
        if (spec.Colors is not null)
            foreach (var (k, v) in spec.Colors)
            {
                int slot = Array.IndexOf(keys, k.ToLowerInvariant());
                if (slot < 0 || v.Equals("default", StringComparison.OrdinalIgnoreCase)) continue;
                string hex = v.TrimStart('#');
                if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb) || rgb is < 0 or > 0xFFFFFF) return OrderResult.No($"couleur invalide : {k}={v}");
                colors[slot] = rgb;
            }
        SocketCreationRegistry.Set(row.Login, new CharacterCreationSpec(name, gender, breedId, head, body, colors, !spec.LeaveTutorial));
        SocketScriptedPickRegistry.Set(row.Login, new ScriptedPick(spec.Server!, null));
        var r = await OrderConnectAsync(row, null, null);
        if (!r.Ok) { SocketCreationRegistry.Clear(row.Login); SocketScriptedPickRegistry.Clear(row.Login); }
        return r;
    }

    /// <summary>Suppression scriptée (compte AVEC code secret) : la réponse n'est jamais journalisée ni conservée après la session.</summary>
    internal async Task<OrderResult> OrderDeleteCharacterAsync(AccountRow row, string? server, string? character, string? answer)
    {
        if (row.Running) return OrderResult.No("déconnecte d'abord le compte");
        if (row.Model.Mode != ConnectionMode.Socket) return OrderResult.No("la suppression de personnage demande le mode Socket");
        if (string.IsNullOrWhiteSpace(server)) return OrderResult.No("serveur requis (nom ou numéro)");
        if (string.IsNullOrWhiteSpace(character)) return OrderResult.No("nom du personnage requis");
        if (string.IsNullOrEmpty(answer)) return OrderResult.No("réponse secrète requise (les comptes sans code secret ne sont pas encore pris en charge)");
        SocketDeletionRegistry.Set(row.Login, new CharacterDeletionSpec(character!.Trim(), answer!));
        SocketScriptedPickRegistry.Set(row.Login, new ScriptedPick(server!, null));
        var r = await OrderConnectAsync(row, null, null);
        if (!r.Ok) { SocketDeletionRegistry.Clear(row.Login); SocketScriptedPickRegistry.Clear(row.Login); }
        return r;
    }

    internal OrderResult OrderDisconnect(AccountRow row)
    {
        if (row.Running) { StopCore(row); UserStopped?.Invoke(row); return OrderResult.Yes; }
        if (row.ReconnectPending) { CancelPendingReconnect(row); return OrderResult.Yes; }
        return OrderResult.Yes;   // déjà hors ligne
    }

    internal async Task<OrderResult> OrderReconnectAsync(AccountRow row)
    {
        if (row.Pid is int pid && pid > 0 && Stores.Launch.LastModeratorPopup((uint)pid) is { } popup
            && DateTime.UtcNow - popup.AtUtc < ModeratorReconnectBan)
            return OrderResult.No("un modérateur a contacté ce perso il y a moins de 2 h");
        if (row.CanReconnect && ReconnectRequested is { } rq) { await rq(row); return OrderResult.Yes; }
        if (!row.Running) return await OrderConnectAsync(row, null, null);
        return OrderResult.Yes;   // déjà en jeu
    }

    internal int OrderDisconnectAll()
    {
        int n = 0;
        foreach (var row in Accounts.ToList())
            if (row.Running) { StopCore(row); UserStopped?.Invoke(row); n++; }
            else if (row.ReconnectPending) { CancelPendingReconnect(row); n++; }
        return n;
    }

    internal void ReloadAccounts() => Reload();
}
