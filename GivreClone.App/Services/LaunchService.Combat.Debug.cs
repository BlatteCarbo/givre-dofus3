using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Combat — diagnostic : dump des paquets de combat et capture découverte =====================
public sealed partial class LaunchService
{
    // ---- DEBUG combat : dump des paquets de combat pour retrouver EMPIRIQUEMENT ou vivent PA/PM/PV/degats. ----
    // Chaque varint est aussi affiche en SIGNE (0xFFFF..=-1, degats negatifs), et les IDs de caracteristiques connus
    // (PA=1, PM=23, PV=0...) sont surlignes. ON pour capturer un combat qui deraille (invocations, LdV...) -> a repasser
    // a false une fois les causes racines trouvees. OFF par defaut (mapping PA/PM/PV fige) -> repasser a true seulement
    // pour re-diagnostiquer un combat qui deraille (le dump kub/Fight est TRES verbeux, il noie la console).
    // OFF (dump combat TRÈS verbeux, réservé au diagnostic dev ; ne PAS activer en prod : ça étouffe le heartbeat licence).
    public bool CombatDebug { get; set; } = false;
    private static readonly System.Collections.Generic.Dictionary<ulong, string> CharIdHints = new()
    {
        [0] = "PV/lifePoints", [1] = "PA/actionPoints", [11] = "vitalite/maxPV?",
        [19] = "portee", [23] = "PM/movementPoints", [96] = "bouclier",
        [29] = "energie", [47] = "energie max", [97] = "PV courants (delta)",
    };

    // Cible du dump : tout paquet recu EN COMBAT, plus les paquets de stats/caracteristiques meme hors combat (placement,
    // feuille de perso) -> on ne rate ni le sync initial (PA/PM/PV max) ni les variations de points / pertes de PV.
    private bool IsCombatDebugTarget(DecodedPacket p)
    {
        if (_inFight.TryGetValue(p.Pid, out var f) && f) return true;
        return p.Name is { } n && (n.Contains("Fight", System.StringComparison.Ordinal)
            || n.Contains("Characteristic", System.StringComparison.Ordinal)
            || n.Contains("LifePoints", System.StringComparison.Ordinal)
            || n.Contains("Stats", System.StringComparison.Ordinal));
    }

    private void DumpCombatPacket(DecodedPacket p)
    {
        if (!LicenseService.IsDeveloper) return;   // arbre de champs + code : outil de reverse, licence développeur uniquement
        var sb = new System.Text.StringBuilder();
        sb.Append("[combat-dbg] ").Append(ProtoLog.Named(p.Name, p.Code)).AppendLine();
        DumpFields(sb, p.Payload, 1);
        TransportLog.Line(sb.ToString().TrimEnd());
    }

    private static void DumpFields(System.Text.StringBuilder sb, IReadOnlyList<ProtoField> fields, int depth)
    {
        foreach (var f in fields)
        {
            sb.Append(' ', depth * 2).Append('#').Append(f.Field).Append(' ');
            switch (f.Wire)
            {
                case WireType.Varint:
                    sb.Append("int ").Append(f.Varint);
                    long sgn = f.AsSignedVarint;
                    if (sgn < 0) sb.Append(" (=").Append(sgn).Append(')');   // 0xFFFF..=-1, deltas/degats negatifs
                    if (CharIdHints.TryGetValue(f.Varint, out var hint)) sb.Append("   <<< id ").Append(hint);
                    break;
                case WireType.I32: sb.Append("i32 ").Append(f.AsI32); break;
                case WireType.I64: sb.Append("i64 ").Append(f.AsI64); break;
                case WireType.Len when f.Message is not null: sb.Append("msg"); break;
                case WireType.Len when f.String is not null: sb.Append("str \"").Append(f.String).Append('"'); break;
                case WireType.Len: sb.Append("bytes[").Append(f.Bytes?.Length ?? 0).Append(']'); break;
                default: sb.Append('?'); break;
            }
            sb.AppendLine();
            if (f.Message is not null) DumpFields(sb, f.Message, depth + 1);
        }
    }

    // ---- CAPTURE DÉCOUVERTE (diagnostic reverse) : dump vers transport-*.log l'arbre de champs des messages qu'on
    // cherche à identifier. Cible : messages NON MAPPÉS (p.Name==null -> code inconnu, à relever) OU messages
    // d'échange/vente déjà nommés (Exchange/Bid -> confirmer les VRAIS numéros de champ wire, les schémas embarqués
    // se contredisent). Exclut le trafic nommé non concerné (bruit). Piloté par AppConfig.PacketDump (toggle Réglages),
    // OFF par défaut. Sert à ouvrir une interface (HDV...) manuellement et relire code + numéros dans le journal. ----
    // EN COMBAT on dump TOUT, y compris les messages DÉJÀ MAPPÉS (avec leur nom) : c'était l'angle mort de cette capture.
    // Sans ça, un code mal pinné (ex `jxh` -> SequenceEndEvent, en réalité {int64,int64}) reste INVISIBLE — il n'apparaît
    // jamais dans le journal puisqu'il est « connu », donc impossible de constater que le pin est faux. Les vrais
    // SequenceStart/End et le déclencheur de tour se relèvent ainsi directement depuis un combat réel.
    private bool IsDiscoveryTarget(DecodedPacket p)
        => AppConfig.Current.PacketDumpAll       // re-liage apres patch : rien ne doit rester masque
           || p.Name is null
           || (_inFight.TryGetValue(p.Pid, out var f) && f)
           || p.Name.Contains("Exchange", System.StringComparison.Ordinal)
           || p.Name.Contains("Bid", System.StringComparison.Ordinal)
           || p.Name.Contains("Shop", System.StringComparison.Ordinal)
           || p.Name.Contains("Subscri", System.StringComparison.Ordinal)
           || p.Name.Contains("Ogrine", System.StringComparison.Ordinal)
           || p.Name.Contains("Kama", System.StringComparison.Ordinal)
           || p.Name.Contains("Bak", System.StringComparison.Ordinal)
           || p.Name.Contains("Account", System.StringComparison.Ordinal)
           || p.Name.Contains("Payment", System.StringComparison.Ordinal)
           || p.Name.Contains("Token", System.StringComparison.Ordinal)
           || p.Name.Contains("Web", System.StringComparison.Ordinal)
           // Lot J : verrous à code / maisons -> relève la trame LockableUseCodeRequest du client manuel (champ #1 à valider).
           || p.Name.StartsWith("Lockable", System.StringComparison.Ordinal)
           || p.Name.StartsWith("House", System.StringComparison.Ordinal)
           // Création de personnage : relève CharacterCreationRequest (sous-structure + octets bruts) et son résultat depuis le vrai client.
           || p.Name.StartsWith("CharacterCreation", System.StringComparison.Ordinal)
           || p.Name.StartsWith("CharacterName", System.StringComparison.Ordinal);

    private void DumpDiscoveryPacket(DecodedPacket p)
    {
        if (!LicenseService.IsDeveloper) return;   // codes + trames brutes : outil de reverse, licence développeur uniquement
        var sb = new System.Text.StringBuilder();
        sb.Append("[pkt-dbg] ").Append(p.ClientToServer ? "C->S " : "S->C ")
          .Append(p.Name ?? "(non-mappe)").Append(" [").Append(p.Code).Append("]  ")
          .Append(p.Payload.Count).Append(" champ(s)");
        // Trame SORTANTE du vrai client : taille + octets BRUTS (petites trames) -> révèle ce que les champs décodés ne
        // montrent pas (uid d'enveloppe, champs par défaut omis, listes packées) pour caler le mode Socket à l'octet près.
        if (p.ClientToServer)
        {
            sb.Append("  ").Append(p.RawFrame.Length).Append('o');
            if (p.RawFrame.Length <= 200) sb.Append(" hex=").Append(Convert.ToHexString(p.RawFrame));
        }
        sb.AppendLine();
        DumpFields(sb, p.Payload, 1);
        TransportLog.Line(sb.ToString().TrimEnd());
    }
}
