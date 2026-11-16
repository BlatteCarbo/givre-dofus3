using System;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Horodatage du dernier CharacterListEvent (y compris une liste vide). La sélection et la carte disent qu'on a quitté cet écran.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, long> _characterListAt = new();

    /// <summary>Le client MITM est sur l'écran de sélection, avant toute entrée en jeu. Une liste vide compte : c'est le cas d'un premier personnage.</summary>
    internal bool IsOnCharacterList(uint pid)
    {
        if (pid == 0 || IsSocketPid(pid) || IsLinkSuspended(pid)) return false;
        if (!_characterListAt.TryGetValue(pid, out long listedAt)) return false;
        if (_clientSelectedAt.TryGetValue(pid, out long selectedAt) && selectedAt >= listedAt) return false;
        if (CurrentMap(pid) is > 0) return false;
        if (_firstMapWait.ContainsKey(pid)) return false;
        return true;
    }

    /// <summary>
    /// Envoie CharacterCreationRequest pendant que ce client est sur la liste. Si l'écran ou le catalogue ne le permet pas,
    /// <see cref="MitmCreationResult.UseSocketInstead"/> est vrai et rien n'est injecté. Un échec pendant que le client
    /// est encore là ne bascule pas vers une deuxième connexion.
    /// </summary>
    internal async Task<MitmCreationResult> CreateOnCharacterListAsync(uint pid, CharacterCreationSpec spec, CancellationToken ct = default)
    {
        if (pid == 0 || !IsOnCharacterList(pid))
            return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseNotOnList);
        ProtocolCatalog? cat;
        try { cat = _catalog ?? MappingCatalog.GetEffective(); }
        catch { cat = null; }
        if (cat is null)
            return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseFields);
        var fields = CharacterCreationTab.ReadFields(cat.CodeForName, cat.FieldNumber);
        if (!fields.Complete)
            return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseFields);

        string name = (spec.Name ?? "").Trim();
        if (name.Length == 0 && !fields.CanSuggest)
            return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseNoSuggestion);

        try
        {
            if (name.Length == 0)
            {
                byte[] ask = MoveForge.BuildCharacterNameSuggestionFramed();
                if (ask.Length == 0)
                    return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseNoSuggestion);
                bool sent = false;
                bool left = false;
                var sug = await SendAndWaitForCodeAsync(pid, () =>
                {
                    if (!IsOnCharacterList(pid)) { left = true; return false; }
                    sent = InjectForge(pid, ask);
                    return sent;
                }, new[] { "CharacterNameSuggestionEvent", "CharacterNameGenerationFailedEvent" }, 8000, ct).ConfigureAwait(false);
                if (sug?.Name == "CharacterNameGenerationFailedEvent")
                    return new MitmCreationResult(false, false, CharacterCreationTab.SuggestionFailed(
                        CharacterCreationTab.SuggestionFailureReason(sug.Payload, cat.FieldNumber)));
                if (sug?.Name == "CharacterNameSuggestionEvent")
                {
                    name = CharacterCreationTab.SuggestionName(sug.Payload, cat.FieldNumber) ?? "";
                    if (name.Length == 0)
                        return new MitmCreationResult(false, false, CharacterCreationTab.SuggestionFailed(null));
                }
                else if (left || !IsOnCharacterList(pid))
                    return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseNotOnList);
                else
                    return new MitmCreationResult(false, false, sent
                        ? CharacterCreationTab.SuggestionFailed(null)
                        : "La demande de nom n'a pas pu partir vers le client. Il reste sur la liste des personnages.");
            }

            if (!IsOnCharacterList(pid))
                return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseNotOnList, name);

            var nums = new CreationFieldNumbers(
                cat.FieldNumber("CharacterRemodelingInformation", "name") ?? 0,
                cat.FieldNumber("CharacterRemodelingInformation", "gender") ?? 0,
                cat.FieldNumber("CharacterRemodelingInformation", "breed_id") ?? 0,
                cat.FieldNumber("CharacterRemodelingInformation", "cosmetic_id") ?? 0,
                cat.FieldNumber("CharacterRemodelingInformation", CharacterCreationTab.BodyField) ?? 0,
                cat.FieldNumber("CharacterRemodelingInformation", "colors") ?? 0);
            byte[] inner;
            try { inner = CharacterCreationEncoder.ModelingInfo(spec with { Name = name }, nums); }
            catch (ArgumentException e)
            {
                bool mapping = e.Message.Contains("mapping", StringComparison.OrdinalIgnoreCase);
                return new MitmCreationResult(mapping, false, mapping
                    ? CharacterCreationTab.SocketBecauseFields
                    : "Création refusée avant envoi : " + e.Message, name);
            }
            byte[] frame = MoveForge.BuildCharacterCreationFramed(inner);
            if (frame.Length == 0)
                return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseFields, name);

            bool createdSent = false;
            bool createdLeft = false;
            var hit = await SendAndWaitForCodeAsync(pid, () =>
            {
                if (!IsOnCharacterList(pid)) { createdLeft = true; return false; }
                createdSent = InjectForge(pid, frame);
                TransportLog.Line($"[création] pid {pid} : envoi {(createdSent ? "OK" : "ECHEC")}.");
                return createdSent;
            }, new[] { "CharacterCreationResultEvent" }, 10000, ct).ConfigureAwait(false);
            if (hit is not null)
            {
                string? refusal = CharacterCreationTab.Refusal(hit.Payload, cat.FieldNumber);
                return refusal is null
                    ? new MitmCreationResult(false, true, CharacterCreationTab.CreatedText, name)
                    : new MitmCreationResult(false, false, refusal, name);
            }
            if (createdLeft || !IsOnCharacterList(pid))
                return new MitmCreationResult(true, false, CharacterCreationTab.SocketBecauseNotOnList, name);
            return new MitmCreationResult(false, false, createdSent
                ? "Le serveur n'a pas répondu à la création."
                : "La demande n'a pas pu partir vers le client. Il reste sur la liste des personnages.", name);
        }
        catch (OperationCanceledException)
        {
            return new MitmCreationResult(false, false, "Création annulée.");
        }
        catch (Exception e)
        {
            return new MitmCreationResult(false, false, "Création non envoyée (" + e.GetType().Name + ").");
        }
    }
}
