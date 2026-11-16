using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GivreClone.App.Services;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// OBSERVATEUR PASSIF du login de connexion MITM (sens client -> serveur). Sur le flux relayé, repère la trame
/// LoginMessage d'identification et en extrait <c>device_identifier</c> (l'empreinte « machine » que le vrai
/// Dofus.exe calcule en SHA-256 hex sur {nom machine, utilisateur, adaptateurs/MAC}). NE MODIFIE JAMAIS les
/// octets : le relais splice à l'identique, l'observateur ne fait que LIRE une copie DÉJÀ transmise.
///
/// But : vérifier dans la Console du bot que l'usurpation de MAC (hook GivreKama, activée par compte) a bien
/// CHANGÉ l'empreinte. On journalise donc la valeur AVANT (dernière vue pour ce compte, persistée d'un lancement
/// à l'autre) et la valeur APRÈS (celle du login courant), avec « changé ✓ » / « inchangé ».
///
/// Le mode Socket (clientless) contrôle déjà sa propre valeur (SocketProbe.DeviceId) -> cet observateur ne
/// concerne QUE le MITM, où c'est le vrai Dofus.exe qui émet la trame.
/// </summary>
public sealed class DeviceIdentifierObserver
{
    private readonly Func<uint, string?> _loginForPid;
    private readonly object _gate = new();

    // Tampon client->serveur par pid, le temps de recadrer la 1re trame (le login peut chevaucher deux lectures TCP).
    // Vidé dès l'extraction. Le login de connexion est TOUJOURS la première chose émise sur le socket de connexion.
    private readonly Dictionary<uint, List<byte>> _buffers = new();

    // pids dont l'identification a déjà été lue sur la connexion COURANTE -> on cesse d'inspecter leurs trames (perf :
    // zéro travail par trame de JEU). Réinitialisé par Forget(pid) (fin de session) -> une reconnexion est re-observée.
    private readonly HashSet<uint> _done = new();

    // Garde-fou mémoire : le login fait ~130 o et arrive en 1re trame. Si on dépasse ça sans identification lisible,
    // c'est un flux qui ne se recadrera pas (ou pas un login) -> on renonce pour ce pid (borne la mémoire, coupe l'O(n²)).
    private const int MaxBufferBeforeLogin = 64 * 1024;

    /// <param name="loginForPid">Résout le pid d'un client MITM vers le login de son compte (clé de persistance AVANT/APRÈS).</param>
    public DeviceIdentifierObserver(Func<uint, string?> loginForPid) => _loginForPid = loginForPid;

    /// <summary>
    /// Alimente l'observateur avec une COPIE d'octets déjà transmis par le relais (purement passif). Seul le sens
    /// client->serveur porte l'identification. Ne lève jamais : l'observation ne doit jamais gêner le transport.
    /// </summary>
    public void Feed(uint pid, bool clientToServer, byte[] data)
    {
        if (!clientToServer || data.Length == 0) return;

        string? device = null;
        try
        {
            lock (_gate)
            {
                if (_done.Contains(pid)) return;

                if (!_buffers.TryGetValue(pid, out var buf)) _buffers[pid] = buf = new List<byte>();
                buf.AddRange(data);

                // Déframe (varint(len)+corps) exactement comme le codec de connexion (ProtoWire.ToFramed) : on réutilise
                // le déframeur du décodeur de capture. Les corps complets sont extraits, le reste incomplet est regardé.
                var frames = RawProtobuf.ConsumeFrames(buf.ToArray(), out var rest);
                buf.Clear();
                buf.AddRange(rest);

                foreach (var frame in frames)
                {
                    if (ConnectionMessages.TryReadDeviceIdentifier(frame, out var d) && d.Length > 0)
                    {
                        device = d;
                        _done.Add(pid);       // identification lue : on n'inspecte plus ce pid jusqu'à la fin de session
                        _buffers.Remove(pid); // tampon inutile désormais
                        break;
                    }
                }

                // Login pas encore vu et tampon anormalement gros -> on renonce (ne bufferisera pas le flux de jeu à l'infini).
                if (device is null && buf.Count > MaxBufferBeforeLogin) { _done.Add(pid); _buffers.Remove(pid); }
            }
        }
        catch { /* observation best-effort : ne casse jamais le transport */ return; }

        // Journalisation HORS verrou (TransportLog réentre chez des abonnés UI).
        if (device is not null) Report(pid, device);
    }

    private void Report(uint pid, string apres)
    {
        string? login = null;
        try { login = _loginForPid(pid); } catch { /* résolution best-effort */ }

        // AVANT = dernière valeur persistée pour CE compte (null au tout premier login observé du compte).
        string? avant = login is { Length: > 0 } ? DeviceIdStore.Get(login) : null;
        bool first = avant is null;
        bool changed = !first && !string.Equals(avant, apres, StringComparison.Ordinal);
        string verdict = first ? "première capture" : (changed ? "changé ✓" : "inchangé");

        // Valeur COMPLÈTE affichée volontairement : c'est un hash (diagnostic local de l'utilisateur, sa propre machine).
        TransportLog.Line(pid, $"[device] device_identifier — AVANT: {avant ?? "(aucun)"}  |  APRÈS: {apres}  ->  {verdict}");

        // La valeur courante devient la référence AVANT du prochain login de ce compte.
        if (login is { Length: > 0 }) DeviceIdStore.Set(login, apres);
    }

    /// <summary>
    /// Oublie l'état d'un pid (fin de session de jeu, ou client arrêté/relancé) : le tampon est libéré et le prochain
    /// login (reconnexion, même pid) est de nouveau observé. Ne touche PAS la valeur persistée (elle sert d'AVANT).
    /// </summary>
    public void Forget(uint pid)
    {
        lock (_gate) { _buffers.Remove(pid); _done.Remove(pid); }
    }
}

/// <summary>
/// Persistance de la DERNIÈRE empreinte <c>device_identifier</c> vue PAR COMPTE (clé = login), pour l'affichage
/// AVANT/APRÈS d'un lancement à l'autre. Fichier %AppData%\GivreClone\device-ids.json, écriture ATOMIQUE (.tmp + Move),
/// contexte STJ source-generated (AOT-safe, réutilise StringMap). Données locales (machine de l'utilisateur), best-effort.
/// </summary>
internal static class DeviceIdStore
{
    private static readonly string PathFile =
        Path.Combine(MZP19LocalClone.DataRoot, "device-ids.json");

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Map = Load();

    public static string? Get(string login)
    {
        lock (Gate) return Map.TryGetValue(login, out var v) ? v : null;
    }

    public static void Set(string login, string deviceId)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(login, out var cur) && cur == deviceId) return;   // inchangé : rien à réécrire
            Map[login] = deviceId;
            Save();
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(PathFile))
                return JsonSerializer.Deserialize(File.ReadAllText(PathFile), AppJsonContext.Default.StringMap) ?? new();
        }
        catch { /* store corrompu -> vide */ }
        return new();
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(PathFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = PathFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Map, AppJsonContext.Default.StringMap));
            File.Move(tmp, PathFile, overwrite: true);
        }
        catch { /* la persistance du diagnostic ne doit jamais casser le transport */ }
    }
}
