using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>Paramètres de login pour le serveur de connexion (étape A).</summary>
internal sealed class SocketLoginParams
{
    public required string DeviceId { get; init; }
    public required string ClientVersion { get; init; }
    public required string Token { get; init; }
    public long CertificateId { get; init; }
    public string CertificateHash { get; init; } = "";
    public ProxySettings? Proxy { get; init; }
    public int PreferredServerId { get; init; }   // 0 = choisir automatiquement le serveur du perso le plus récent
    public string? PreferredCharacter { get; init; }

    /// <summary>Sélecteur INTERACTIF (mode « écran de choix du serveur / du personnage ») : reçoit la liste des serveurs
    /// après jointure des métadonnées d'identification avec la CharacterListResponse séparée, puis renvoie
    /// (serverId, characterName?) choisi, ou null =
    /// annulé (abandon). Null ici = pas de sélection interactive (auto : PreferredServerId / perso le plus récent).</summary>
    public Func<IReadOnlyList<ConnServer>, CancellationToken, Task<(int ServerId, string? Character)?>>? ChooseServerChar { get; init; }

    /// <summary>Création de personnage demandée : un serveur sans personnage devient un choix valable. Lu après le choix, car l'écran l'enregistre à ce moment.</summary>
    public Func<bool>? CreationPending { get; init; }
}

/// <summary>
/// Étape A du mode Socket : ouvre NOTRE propre socket vers le serveur de connexion Dofus 3, s'authentifie avec le
/// jeton HAAPI (aucun Dofus.exe), lit la liste des serveurs, sélectionne un serveur et récupère le host + jeton du
/// serveur de JEU. S'arrête là (l'entrée en jeu + la vérification Schnorr = étapes B/C). Tout est journalisé en
/// [socket*] pour un debug complet. Le serveur de jeu, lui, se traite ensuite sur une seconde connexion.
/// </summary>
internal sealed class SocketConnectionClient
{
    private const string ConnectionHost = "dofus2-co-production.ankama-games.com";
    private const int ConnectionPort = 5555;
    private const int OverallTimeoutMs = 30_000;

    private readonly uint _logPid;   // 0 = console globale (un compte socket n'a pas de pid Dofus.exe)

    /// <summary>Version exigée par le serveur (renvoyée sur OUTDATED/INVALID_CLIENT_VERSION) : SocketProbe retente avec.</summary>
    public string? RequiredVersion { get; private set; }

    /// <summary>Raison du refus d'identification (enum IdentificationResponse.Error), -1 = pas de refus. Sert à distinguer
    /// un refus DÉFINITIF (banni, identifiants, Bouclier…) d'un refus passager (déjà connecté, OTP) côté reconnexion.</summary>
    public int IdErrorReason { get; private set; } = -1;
    /// <summary>Fin du bannissement (ban_end_date) quand IdErrorReason = BANNED ; vide sinon ou sans date.</summary>
    public string BanEndDate { get; private set; } = "";
    /// <summary>Refus de sélection serveur (enum SelectServerResponse.Error), -1 = pas de refus.</summary>
    public int SelectErrorReason { get; private set; } = -1;
    /// <summary>Le compte n'a AUCUN personnage (sur aucun serveur) : rien à connecter.</summary>
    public bool NoCharacter { get; private set; }
    public bool SelectionRejected { get; private set; }

    private int _selectedServerId;   // serveur choisi (PickServer) : reporté dans le ConnDecoded de succès

    /// <summary>Personnage choisi dans le sélecteur interactif (mode « choix du perso »), à sélectionner à l'étape B par nom ;
    /// null = aucun (choix serveur seul, ou pas de sélecteur).</summary>
    public string? ChosenCharacter { get; private set; }
    /// <summary>L'utilisateur a ANNULÉ la sélection interactive : abandon PROPRE (la boucle de session ne reconnecte pas).</summary>
    public bool UserCancelled { get; private set; }

    public SocketConnectionClient(uint logPid = 0) => _logPid = logPid;

    /// <summary>Renvoie le résultat de sélection serveur (host + jeton de jeu) en cas de succès, sinon null.</summary>
    public async Task<ConnDecoded?> RunAsync(SocketLoginParams p, CancellationToken ct = default)
    {
        AccountSubscriptionInfo.Clear(_logPid);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OverallTimeoutMs);
        var tk = timeout.Token;

        Stream? stream = null;
        try
        {
            // 1) DNS
            SocketLog.Info(_logPid, $"résolution DNS de {ConnectionHost}…");
            var addrs = await Dns.GetHostAddressesAsync(ConnectionHost, tk).ConfigureAwait(false);
            var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip is null) { SocketLog.Info(_logPid, "aucune IPv4 pour le serveur de connexion — abandon"); return null; }
            var ipv4 = ip.GetAddressBytes();
            var via = p.Proxy is null ? "DIRECT (IP réelle)" : $"{p.Proxy.Kind} {p.Proxy.Host}:{p.Proxy.Port}";
            SocketLog.Info(_logPid, $"cible {ip}:{ConnectionPort} via {via}");
            ConnectionJournal.ForBot(_logPid, Loc.Fmt("L.Journal.ConnServer", $"{ip}:{ConnectionPort}"));

            // 2) dial (même politique que le relais MITM : bind-direct adaptateur / SOCKS5 / HTTP CONNECT / direct)
            stream = await SocketDialer.DialAsync(p.Proxy, ipv4, ConnectionPort, _logPid, "[connexion]", tk).ConfigureAwait(false);
            SocketLog.Info(_logPid, "connexion TCP établie");

            // 3) login
            var loginFrame = ConnectionMessages.BuildIdentification(
                uuid: "0", deviceId: p.DeviceId, clientVersion: p.ClientVersion,
                token: p.Token, certificateId: p.CertificateId, certificateHash: p.CertificateHash);
            SocketLog.Auth(_logPid, $"IdentificationRequest : version={p.ClientVersion} " +
                                    $"devicePresent={(p.DeviceId.Length > 0).ToString().ToLowerInvariant()} " +
                                    $"tokenPresent={(p.Token.Length > 0).ToString().ToLowerInvariant()} " +
                                    $"shieldPresent={(p.CertificateId != 0 || p.CertificateHash.Length > 0).ToString().ToLowerInvariant()}");
            SocketLog.Tx(_logPid, $"LoginMessage/Identification {loginFrame.Length}o");   // jamais le contenu : jeton d'authentification du compte
            await stream.WriteAsync(loginFrame, tk).ConfigureAwait(false);
            await stream.FlushAsync(tk).ConfigureAwait(false);

            // 4) boucle de lecture déframée (varint(len) + corps)
            var acc = new List<byte>(4096);
            var read = new byte[8192];
            bool selecting = false;
            IReadOnlyList<ConnServer>? serverMetadata = null;
            IReadOnlyList<ConnServer>? characterBuckets = null;
            while (!tk.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(read, tk).ConfigureAwait(false);
                if (n <= 0) { SocketLog.Info(_logPid, "le serveur a fermé la connexion"); return null; }
                acc.AddRange(read.AsSpan(0, n).ToArray());
                SocketLog.Rx(_logPid, $"{n}o reçus (tampon {acc.Count}o)");

                while (TryDeframe(acc, out var frameBody))
                {
                    // Ne jamais journaliser le payload connexion brut : les réponses peuvent porter nickname,
                    // personnages et jeton du serveur de jeu. La taille suffit au diagnostic de framing.
                    SocketLog.Rx(_logPid, $"trame {frameBody.Length}o reçue");
                    var d = ConnectionMessages.Decode(frameBody);
                    if (d.Type is ConnDecoded.Kind.IdentificationSuccess or ConnDecoded.Kind.SelectServerSuccess)
                        Stores.Launch.RememberMirrorLogin(_logPid, d.Type.ToString(), WorldMirrorFraming.Prefix(frameBody));
                    if (d.Type == ConnDecoded.Kind.IdentificationSuccess)
                        SocketLog.Info(_logPid, "[diag] structure de la trame d'identification (sans valeurs sensibles) : " + ConnectionMessages.Shape(frameBody));
                    if (d.Type == ConnDecoded.Kind.IdentificationSuccess)
                    {
                        serverMetadata = d.Servers.ToArray();
                        // Les versions ne fournissent pas toutes les personnages au même endroit.
                        // NE PAS déduire "aucun personnage" d'un IdentificationSuccess qui
                        // ne comporte que les serveurs : attendre CharacterListResponse.
                        // Le marqueur provient du décodeur de CE message, et non d'un numéro
                        // de version supposé. Si CharacterList arrive avant, on la conserve.
                        if (d.CharactersEmbedded) characterBuckets ??= serverMetadata;
                    }
                    else if (d.Type == ConnDecoded.Kind.CharacterList)
                        characterBuckets = d.Servers.ToArray();

                    var result = Handle(d, p, stream, ref selecting, tk);
                    if (result is not null) return result;                    // succès sélection serveur -> étape A OK
                    if (NoCharacter || d.Type is ConnDecoded.Kind.IdentificationError
                                                 or ConnDecoded.Kind.SelectServerError) return null;

                    // 3.6.12.16 sépare les métadonnées serveur (IdentificationSuccess) et les personnages
                    // (CharacterListResponse). Ne jamais décider avant d'avoir les DEUX, quel que soit leur ordre d'arrivée.
                    if (!selecting && serverMetadata is not null && characterBuckets is not null)
                    {
                        var selectable = ConnectionMessages.MergeServerCharacters(serverMetadata, characterBuckets);
                        int characterCount = selectable.Sum(s => s.Characters.Count);
                        SocketLog.Info(_logPid, $"liste personnages reçue : {characterCount} personnage(s) sur {selectable.Count} serveur(s)");
                        if (characterCount <= 0 && p.ChooseServerChar is null)
                        { SocketLog.Info(_logPid, "aucun serveur avec un personnage — abandon"); NoCharacter = true; return null; }

                        int chosenServerId;
                        string? chosenCharacter = null;
                        if (p.ChooseServerChar is { } choose)
                        {
                            // Lecture suspendue pendant la modale ; le délai humain n'est pas facturé au timeout réseau.
                            (int ServerId, string? Character)? pick;
                            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                            try { pick = await choose(selectable, ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception e) { SocketLog.Info(_logPid, $"sélecteur serveur/perso : {e.GetType().Name} — abandon"); return null; }
                            finally { timeout.CancelAfter(OverallTimeoutMs); }
                            if (pick is not { } chosen || chosen.ServerId <= 0)
                            { SocketLog.Info(_logPid, "sélection serveur/perso annulée ou invalide — abandon"); UserCancelled = true; return null; }

                            var chosenServer = selectable.FirstOrDefault(s => s.Id == chosen.ServerId && (s.Characters.Count > 0 || p.CreationPending?.Invoke() == true));
                            if (chosenServer is null ||
                                (chosen.Character is not null &&
                                 !chosenServer.Characters.Any(c => string.Equals(c.Name, chosen.Character, StringComparison.Ordinal))))
                            {
                                SocketLog.Info(_logPid, "sélection serveur/perso incohérente avec la liste serveur reçue — abandon");
                                UserCancelled = true;
                                return null;
                            }
                            chosenServerId = chosen.ServerId;
                            chosenCharacter = chosen.Character;
                        }
                        else
                        {
                            chosenServerId = PickServer(selectable, p.PreferredServerId);
                            if (chosenServerId <= 0)
                            { SocketLog.Info(_logPid, "serveur explicite absent ou aucun serveur sélectionnable — abandon"); SelectionRejected = true; return null; }
                            chosenCharacter = p.PreferredCharacter;
                            if (!string.IsNullOrWhiteSpace(chosenCharacter)
                                && !selectable.First(s => s.Id == chosenServerId).Characters.Any(c =>
                                    string.Equals(c.Name, chosenCharacter, StringComparison.Ordinal)))
                            {
                                SocketLog.Info(_logPid, "personnage explicite absent du serveur choisi — aucun repli");
                                SelectionRejected = true;
                                return null;
                            }
                        }

                        _selectedServerId = chosenServerId;
                        ChosenCharacter = chosenCharacter;
                        selecting = true;
                        var sel = ConnectionMessages.BuildSelectServer("1", chosenServerId);
                        SocketLog.Info(_logPid, $"→ serveur choisi {chosenServerId}" +
                                                (chosenCharacter is not null ? ", personnage explicite" : " (perso auto)"));
                        SocketLog.Tx(_logPid, $"SelectServerRequest {sel.Length}o");
                        try { await stream.WriteAsync(sel, tk).ConfigureAwait(false); await stream.FlushAsync(tk).ConfigureAwait(false); }
                        catch (Exception e) { SocketLog.Info(_logPid, $"envoi sélection échoué : {e.GetType().Name}"); return null; }
                    }
                }
            }
            return null;
        }
        catch (OperationCanceledException) { SocketLog.Info(_logPid, "délai dépassé / annulé"); return null; }
        catch (Exception e) { SocketLog.Info(_logPid, $"ERREUR {e.GetType().Name} : {e.Message}"); return null; }
        finally { try { stream?.Dispose(); } catch { } }
    }

    // Traite un message décodé ; renvoie non-null quand la sélection serveur a réussi (fin de l'étape A).
    private ConnDecoded? Handle(ConnDecoded d, SocketLoginParams p, Stream stream, ref bool selecting, CancellationToken tk)
    {
        switch (d.Type)
        {
            case ConnDecoded.Kind.IdentificationSuccess:
                SocketLog.Info(_logPid, $"✔ AUTHENTIFIÉ — {d.Servers.Count} serveur(s) reçu(s); personnages lus dans la réponse : " + d.Servers.Sum(x => x.Characters.Count) + (d.CharactersEmbedded ? "" : " (aucun : attente d'une liste séparée)"));
                ConnectionJournal.ForBot(_logPid, Loc.T("L.Journal.Identified"), LuaLogLevel.Success);
                AccountSubscriptionInfo.Set(_logPid, d.SubscriptionEndRaw, d.UnlimitedAccess, d.InfiniteSubscription);   // abonnement annoncé à la connexion (même source que GivreActive)
                SocketLog.Info(_logPid, "abonnement : fin=" + (string.IsNullOrEmpty(d.SubscriptionEndRaw) ? "(aucune)" : d.SubscriptionEndRaw) + (d.UnlimitedAccess ? " · accès illimité" : "") + (d.InfiniteSubscription ? " · abonnement infini" : ""));
                foreach (var s in d.Servers)
                {
                    var status = s.StatusText;
                    SocketLog.Info(_logPid, $"    serveur {s.Id} ({status}), accessibilité={s.Accessibility}" + (s.Characters.Count > 0 ? " : " + string.Join(", ", s.Characters.Select(c => $"{c.Name} niv{c.Level}")) : ""));
                }
                return null;

            case ConnDecoded.Kind.CharacterList:
                SocketLog.Rx(_logPid, "CharacterListResponse reçue; jointure différée avec les métadonnées serveur");
                return null;

            case ConnDecoded.Kind.IdentificationError:
                var name = ConnectionMessages.IdErrorName(d.ErrorReason);
                var extra = d.ErrorReason == 13 || d.ErrorReason == 12 ? $" (required_version={d.RequiredVersion})"
                          : d.ErrorReason == 3 ? $" (ban_end={d.BanEndDate})" : "";
                SocketLog.Info(_logPid, $"✘ IDENTIFICATION REFUSÉE : {name}{extra}");
                IdErrorReason = d.ErrorReason;
                if (d.ErrorReason == 3) BanEndDate = d.BanEndDate ?? "";
                if ((d.ErrorReason == 12 || d.ErrorReason == 13) && !string.IsNullOrEmpty(d.RequiredVersion))
                    RequiredVersion = d.RequiredVersion;   // le serveur nous donne la bonne version -> SocketProbe retente
                return null;

            case ConnDecoded.Kind.SelectServerSuccess:
                d.SelectedServerId = _selectedServerId;   // serveur déterministe -> renseigne row.Server tout de suite
                SocketLog.Info(_logPid, $"✔ SERVEUR SÉLECTIONNÉ — étape A réussie. host de JEU = {d.GameHost} " +
                                        $"ports={string.Join("/", d.GamePorts)} tokenPresent={(d.GameToken.Length > 0).ToString().ToLowerInvariant()}");
                SocketLog.Info(_logPid, "→ la suite (connexion au serveur de jeu + vérification Schnorr) = étape B, à venir.");
                return d;

            case ConnDecoded.Kind.SelectServerError:
                SocketLog.Info(_logPid, $"✘ SÉLECTION SERVEUR REFUSÉE : {ConnectionMessages.SelectErrorName(d.SelectServerError)}");
                SelectErrorReason = d.SelectServerError;
                return null;

            default:
                SocketLog.Rx(_logPid, $"message ignoré (type {d.Type})");
                return null;
        }
    }

    // Choisit le serveur : préféré s'il est fourni et présent, sinon celui du perso à la dernière connexion la plus récente.
    private static int PickServer(IReadOnlyList<ConnServer> servers, int preferred)
    {
        if (preferred > 0) return servers.Any(s => s.Id == preferred && s.Characters.Count > 0) ? preferred : 0;
        int best = 0; DateTimeOffset newest = DateTimeOffset.MinValue;
        foreach (var s in servers)
            foreach (var c in s.Characters)
                if (DateTimeOffset.TryParse(c.LastConnectionDate, out var when) && when > newest) { newest = when; best = s.Id; }
        if (best == 0) best = servers.FirstOrDefault(s => s.Characters.Count > 0)?.Id ?? 0;
        return best;
    }

    // Extrait UNE trame (varint(len)+corps) du tampon si elle est complète ; la retire du tampon. false = incomplète.
    private static bool TryDeframe(List<byte> acc, out byte[] body)
    {
        body = Array.Empty<byte>();
        // lit le varint de longueur sans le consommer si la trame n'est pas entière
        int len = 0, shift = 0, i = 0;
        while (true)
        {
            if (i >= acc.Count) return false;                 // longueur incomplète
            byte b = acc[i++];
            len |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 35) { acc.Clear(); return false; }    // garde-fou varint corrompu
        }
        if (acc.Count - i < len) return false;                // corps incomplet
        body = new byte[len];
        acc.CopyTo(i, body, 0, len);
        acc.RemoveRange(0, i + len);
        return true;
    }

}
