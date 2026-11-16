using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Mapping de protocole STREAMÉ du serveur Ikuware, déchiffré + monté EN MÉMOIRE (jamais écrit sur disque, donc
/// jamais visible pour l'utilisateur). Récupéré une fois après un login réussi (jeton de session), puis consommé
/// par <see cref="LaunchService"/> à la place du data/ local. En dev (data/ présent), le repli local reste utilisé
/// si le stream échoue ou est indisponible (pas de session, bypass DEV…).
/// </summary>
public static class MappingCatalog
{
    // MZP11: derive the exact requested game build from the installed Cytrus
    // SQLite game manifest, rather than freezing the earlier 3.7.10.10. This
    // local evidence is still a candidate, NOT a live protocol certificate.

    private static ProtocolCatalog? _streamed;
    private static MZP12SocketVersionBinding.Binding? _sessionVersionBinding;
    private static Task? _inflight;
    private static readonly object _gate = new();
    // ++ à chaque Clear : un fetch parti AVANT une purge (kill switch, fin de session) ne réinstalle pas le mapping en finissant.
    private static int _clearEpoch;

    /// <summary>Safe structural version verdict, not the mapping contents or an authentication proof.</summary>
    public static MZP10MappingVersionAdmission.Decision? LastVersionDecision { get; private set; }

    /// <summary>Catalogue streamé prêt, ou null (pas encore récupéré / échec / pas de session -> repli data/ local).</summary>
    public static ProtocolCatalog? Streamed => _streamed;

    public static bool IsReady => _streamed is not null || (LocalEdition.Enabled && GetEffective() is not null);
    /// <summary>Only the build/hash pair attached to an accepted catalogue.</summary>
    public static MZP12SocketVersionBinding.Binding? SessionVersionBinding
    {
        get { lock (_gate) return _sessionVersionBinding ?? (LocalEdition.Enabled ? LocalEditionBinding() : null); }
    }

    // ---- édition locale : mapping de data/ + déclaration écrite du build (data/bundle-build.json) vérifiée contre l'installation
    private static ProtocolCatalog? _localEdition;
    private static MZP12SocketVersionBinding.Binding? _localBinding;
    private static long _localBindingAt;

    private static ProtocolCatalog? LoadLocalEdition()
    {
        try
        {
            var cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
            var allMissing = RequiredMapping.MissingIn(cat.FieldNumber);
            // Lacunes CONNUES et sans effet sur le chemin client officiel + proxy : champ de la connexion par socket direct, renommé
            // dans le mapping 3.7 (fyvi/fyvj présents, fyvk absent). Le chemin socket reste donc non opérationnel ; le reste est publié.
            var known = new HashSet<string> { "ClientChallengeInitRequest.fyvk" };
            var missing = allMissing.Where(m => !known.Contains(m)).ToList();
            foreach (var m in allMissing.Where(known.Contains)) Logger.Warn($"Mapping local : lacune connue {m} (socket direct non opérationnel).");
            MissingRequiredFields = missing;
            if (!MZP1MappingPublicationPolicy.HasCompleteRequirements(cat.MappingCount, Math.Max(cat.MessageDefCount, cat.FieldMessageCount), missing))
            { Logger.Error($"Mapping local incomplet : {missing.Count} champ(s) requis absents ; non publié."); return null; }
            Logger.Info($"Mapping local : {cat.MappingCount} codes, {cat.FieldMessageCount} messages à champs (fields.json), {cat.MessageDefCount} définitions .proto.");
            return cat;
        }
        catch (Exception e) { Logger.Error($"Mapping local illisible ({e.Message})."); return null; }
    }

    /// <summary>Diagnostic read-only du mapping local face à l'installation choisie.
    /// Returns a candidate even when the build differs: MZP12 still refuses
    /// the incompatible session, but can show the precise pair of versions
    /// instead of falsely saying no mapping is published. No server ACK.</summary>
    private static MZP12SocketVersionBinding.Binding? LocalEditionBinding()
    {
        long now = Environment.TickCount64;
        if (_localBindingAt != 0 && now - _localBindingAt < 20_000) return _localBinding;
        _localBindingAt = now; _localBinding = null;
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "data", "bundle-build.json");
            if (!File.Exists(file)) { Logger.Info("Mapping local : data/bundle-build.json absent, aucun build déclaré."); return null; }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            string? declared = doc.RootElement.TryGetProperty("build", out var b) ? b.GetString() : null;
            if (!MZP10MappingVersionAdmission.TryParse(declared, out _, out _, out _, out _)) { Logger.Error("Mapping local : build déclaré invalide."); return null; }
            var exe = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(AppConfig.Current.DofusExePath);
            var installed = MZP11CytrusVersionProbe.InspectInstalledDofus(exe);
            if (!installed.HasLocalVersionEvidence || string.IsNullOrWhiteSpace(installed.ManifestSha256)) { Logger.Error("Mapping local : version installée illisible."); return null; }
            _localBinding = new(declared, installed.ManifestSha256);
            if (!MZP10MappingVersionAdmission.IsSameProtocolFamily(installed.ProtocolVersion, declared))
                Logger.Warn($"Mapping local {declared} incompatible avec la famille du jeu installé {installed.ProtocolVersion}.");
            else
                Logger.Info($"Mapping local lié au client installé {declared} (empreinte {installed.ManifestSha256[..8]}…).");
        }
        catch (Exception e) { Logger.Error($"Mapping local : liaison impossible ({e.Message})."); }
        return _localBinding;
    }

    /// <summary>Champs que CE build lit ou forge (manifeste <see cref="RequiredMapping"/>) absents du bundle streamé reçu,
    /// « Message.champ ». Vide = le bundle couvre tout le code. Calculé à chaque réception du mapping.</summary>
    public static IReadOnlyList<string> MissingRequiredFields { get; private set; } = Array.Empty<string>();

    /// <summary>Le catalogue streamé vient de devenir disponible (thread de fond -> marshaller pour l'UI si besoin).</summary>
    public static event Action? Ready;

    /// <summary>Le mapping vient d'être PURGÉ (<see cref="Clear"/> : kill/ban/logout). Les consommateurs (décodeur)
    /// doivent lâcher leur référence au catalogue et couper le décodage -> bot aveugle + joyau hors RAM.</summary>
    public static event Action? Cleared;

    /// <summary>Récupère le mapping une seule fois (idempotent, best-effort : ne lève jamais). À appeler/awaiter
    /// après un login réussi. Sans session (bypass DEV, échec) -> ne fait rien et on retombe sur le data/ local.</summary>
    public static Task EnsureFetchedAsync()
    {
        lock (_gate)
        {
            if (_streamed is not null) return Task.CompletedTask;
            // Requête signée refusée sans nouvel essai possible : on ne re-demande pas (pas de boucle) jusqu'au prochain login.
            if (LicenseService.SigningBlocked) return Task.CompletedTask;
            return _inflight ??= FetchCoreAsync(_clearEpoch);
        }
    }

    private static async Task FetchCoreAsync(int epoch)
    {
        try
        {
            var selectedExe = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(
                AppConfig.Current.DofusExePath);
            var installed = MZP11CytrusVersionProbe.InspectInstalledDofus(selectedExe);
            if (!installed.HasLocalVersionEvidence)
            {
                Logger.Error("Mapping refusé : version réelle du manifeste Cytrus non lisible. " + installed.Diagnostic);
                lock (_gate) if (epoch == _clearEpoch)
                    LastVersionDecision = MZP10MappingVersionAdmission.Evaluate(null);
                return;
            }
            string requestedBuild = installed.ProtocolVersion!;
            var bundle = await LicenseService.FetchMappingAsync(requestedBuild).ConfigureAwait(false);
            if (bundle is null) { Logger.Info("Mapping : pas de session -> repli local."); return; }
            if (bundle.NotModified)
            {
                // Without a cache owned by this exact client/source, a server
                // 304 is not enough to publish a usable catalogue.
                Logger.Info("Mapping : not_modified sans bundle certifié local, aucune publication.");
                return;
            }

            var stillInstalled = MZP11CytrusVersionProbe.InspectInstalledDofus(selectedExe);
            if (!stillInstalled.HasLocalVersionEvidence ||
                !MZP10MappingVersionAdmission.IsSameProtocolFamily(stillInstalled.ProtocolVersion, installed.ProtocolVersion))
            {
                Logger.Error("Mapping refusé : l'installation Dofus a changé pendant la récupération.");
                return;
            }

            // A signed/encrypted response is evidence of its provider, NOT that
            // its mapping applies to the currently installed Dofus.exe. Reject
            // a different protocol family; patch/build and fingerprint drift
            // within the family do not invalidate the mapping.
            var versionAdmission = MZP10MappingVersionAdmission.Evaluate(new(
                requestedBuild, bundle.Build, bundle.Fallback, bundle.Artefacts));
            lock (_gate) if (epoch == _clearEpoch) LastVersionDecision = versionAdmission;
            if (!versionAdmission.ProtocolFamilyCandidate)
            {
                Logger.Error($"Mapping version refusée ({versionAdmission.Status}) : {versionAdmission.SafeReason}");
                return;
            }

            var a = bundle.Artefacts;
            var cat = ProtocolCatalog.LoadFromStrings(
                Get(a, "proto"), Get(a, "matching"), Get(a, "confirmed"), Get(a, "fields"));
            // CONTRÔLE DU BUNDLE REÇU : chaque champ que ce build lit ou forge doit exister dans le bundle servi. Le 04/10 la
            // prod tournait sur un bundle différent de celui testé (poussées, morts, PV de combat jamais lus) sans aucun signal
            // avant qu'un combat passe par là. Vérifié AVANT le repli local de dev : seulement ce que le serveur a envoyé.
            var missingFields = RequiredMapping.MissingIn(cat.FieldNumber);
            // A partial catalogue can decode *some* events, but it must not
            // become the effective production catalogue: consumers would
            // otherwise silently emit incorrectly tagged actions. This is
            // a structural gate, not proof of source or wire authenticity.
            if (!MZP1MappingPublicationPolicy.HasCompleteRequirements(
                    cat.MappingCount, cat.MessageDefCount, missingFields))
            {
                lock (_gate)
                {
                    if (epoch != _clearEpoch) return;
                    MissingRequiredFields = missingFields;
                }
                Logger.Error($"Mapping streamé rejeté : catalogue incomplet, " +
                             $"{missingFields.Count} champ(s) requis absents; aucun mapping streamé publié.");
                return;
            }
#if DEBUG
            // DEV : un mapping streamé PÉRIMÉ/incomplet (ex : sous-types imbriqués PlayerSearch/SearchByCharacterName
            // pas encore re-déployés côté serveur) est comblé par le mapping LOCAL complet -> on itère sans attendre le
            // re-déploiement serveur, sans RIEN coder en dur (toujours piloté par fichiers). COMPILÉ HORS PROD (le streamé
            // y fait autorité seul ; c'est au serveur d'être à jour).
            try { cat.Fallback = _localFallback ??= ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")); }
            catch (Exception fx) { Logger.Info($"Mapping : repli local DEV indisponible ({fx.Message})."); }
#endif
            lock (_gate)
            {
                if (epoch != _clearEpoch) { Logger.Info("Mapping : purgé pendant la récupération -> ignoré."); return; }
                _streamed = cat;
                _sessionVersionBinding = new(bundle.Build, stillInstalled.ManifestSha256);
                MissingRequiredFields = missingFields;
            }
            Logger.Info($"Mapping streamé : build={bundle.Build} v{bundle.MappingVersion} fallback={bundle.Fallback} " +
                        $"-> {cat.MappingCount} codes, {cat.MessageDefCount} messages.");
            Logger.Info($"Mapping streamé : les {RequiredMapping.Scanned.Length + RequiredMapping.Extra.Length} champs requis par ce build sont présents.");
            Ready?.Invoke();
        }
        catch (Exception e)
        {
            Logger.Error($"Mapping : récupération échouée ({e.Message}) -> repli local si présent.");
        }
        finally
        {
            lock (_gate) if (epoch == _clearEpoch) _inflight = null;   // après une purge, _inflight appartient au fetch suivant
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;

    /// <summary>PURGE le mapping streamé de la mémoire (kill / ban / révocation / expiration / logout). En prod,
    /// <see cref="GetEffective"/> redevient null -> le décodeur devient AVEUGLE en secondes, INDÉPENDAMMENT d'un
    /// patch d'un bool client : un re-fetch exige un jeton de session VIVANT (que le serveur refuse alors). Referme
    /// le trou "grab-and-hold" (une session bannie qui continue de botter jusqu'à fermeture) ET réduit la fenêtre
    /// d'extraction RAM du joyau. En DEV (Debug) le repli data/ local n'est PAS purgé (dev sans serveur).</summary>
    public static void Clear()
    {
        lock (_gate)
        {
            _streamed = null;
            _sessionVersionBinding = null;
            _inflight = null;
            _clearEpoch++;
            LastVersionDecision = null;
            MissingRequiredFields = Array.Empty<string>();
        }
        Cleared?.Invoke();   // le décodeur en cours lâche sa référence + coupe le feed (LaunchService)
        Logger.Info("Mapping purgé (session non autorisée).");
    }

    // -------------------------------------------------- diagnostic (avertissement au lancement) --

#if DEBUG
    private static ProtocolCatalog? _localFallback;   // repli data/ local : DEV uniquement (absent du prod)
#endif

    /// <summary>Catalogue EFFECTIF utilisé pour décoder. C'est la même source que <see cref="LaunchService"/>.
    ///
    /// PROD (Release) : UNIQUEMENT le mapping streamé + déchiffré EN MÉMOIRE (jeton de session). Retourne null tant
    /// qu'aucune session valide n'a fourni le mapping -> « pas de session ⇒ rien de décodable » (propriété défensive :
    /// le joyau — le mapping — n'existe JAMAIS sur disque à côté de l'exe, seul le serveur le délivre par session).
    ///
    /// DEV (Debug) : repli data/ local si le stream est absent, pour bosser sans serveur (bypass GIVRE-DEV, etc.).
    /// Le repli local est chargé + mis en cache une fois. Ce chemin est COMPILÉ HORS du prod (comme GIVRE-DEV).</summary>
    public static ProtocolCatalog? GetEffective()
    {
        if (LocalEdition.Enabled) return _streamed ?? (_localEdition ??= LoadLocalEdition());
#if DEBUG
        return _streamed ?? (_localFallback ??= ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));
#else
        return _streamed;   // prod : mapping en mémoire seulement (jamais de repli disque)
#endif
    }

    // Messages que le bot DOIT pouvoir décoder pour fonctionner (map / mouvement / perso). S'ils ne se résolvent
    // pas (ni matching_current.json ni confirmed.json), le bot "ne voit pas les paquets" -> on avertit.
    private static readonly string[] Essential =
    {
        "MapComplementaryInformationEvent", "MapMovementEvent", "MapMovementRequest",
        "MapMovementConfirmRequest", "CharacterListEvent", "GameRolePlayShowActorsEvent",
        "ContextRemoveElementEvent", "MapChangeRequest",
    };

    /// <summary>Liste des messages ESSENTIELS non résolus par le mapping effectif (vide = OK). proto absent => tout
    /// manque. À vérifier avant de lancer un compte : non vide => avertir l'utilisateur (fichiers manquants/incomplets).</summary>
    public static IReadOnlyList<string> MissingEssentials()
    {
        ProtocolCatalog? cat;
        try { cat = GetEffective(); }
        catch { return Essential; }   // même pas de catalogue chargeable -> tout manque
        if (cat is null || Math.Max(cat.MessageDefCount, cat.FieldMessageCount) == 0) return Essential;   // pas de session/mapping -> rien de décodable (le .proto n'est pas livré en édition locale : fields.json fait foi)

        var missing = new List<string>();
        foreach (var name in Essential)
            if (cat.CodeForName(name) is null) missing.Add(name);
        return missing;
    }
}
