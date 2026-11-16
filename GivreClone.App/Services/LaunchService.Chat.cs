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

// ===================== Messagerie : MP privé et message de canal =====================
public sealed partial class LaunchService
{
    /// <summary>Envoie un MP privé à `target` depuis le bot `pid` (forge ChatPrivateMessageRequest ktb, code résolu par
    /// NOM via le mapping -> patch-robuste). Renvoie false si non autorisé (licence), code non mappé, ou relais absent.</summary>
    public bool SendPrivateMessage(uint pid, string target, string content)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return false;   // messagerie = Perm.PmRelay au POINT D'ACTION (le verbe Lua sendPrivateMessage + l'UI SendChat contournent le gate de PrivateMessageService)
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(content)) return false;
        // AUDIT L7 : garde-fou à la SOURCE de la forge -> retire les caractères de contrôle (dont \r\n : anti-injection de
        // multi-lignes/commandes) et borne à 240 (cap Dofus), quel que soit l'appelant (auto, manuel, futur).
        content = new string(content.Where(c => c >= ' ' || c == '\t').ToArray()).Trim();
        if (content.Length == 0) return false;
        if (content.Length > 240) content = content[..240];
        var relay = RelayForInject();
        if (relay is null) return false;
        string? code = _catalog?.CodeForName("ChatPrivateMessageRequest");   // kql, résolu du mapping streamé
        if (code is null) { TransportLog.Line("[mp] envoi impossible : ChatPrivateMessageRequest non mappé."); return false; }
        byte[] pkt = MoveForge.BuildChatPrivateMessageFramed(code, target.Trim(), content);
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        return ok;
    }

    /// <summary>Envoie un message sur un CANAL (`channel` : 0 général, 5 commerce, 6 recrutement…) depuis le bot `pid`
    /// (forge ChatChannelMessageRequest kqd, code + champs résolus par nom -> patch-robuste). Même durcissement que le
    /// MP : caractères de contrôle retirés (anti-injection \r\n), longueur bornée à 240. false si non mappé / pas de relais.</summary>
    public bool SendChannelMessage(uint pid, int channel, string content)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return false;   // messagerie canal = Perm.PmRelay au POINT D'ACTION (anti-spam commerce/recrutement par un tier sans messagerie)
        if (string.IsNullOrWhiteSpace(content)) return false;
        content = new string(content.Where(c => c >= ' ' || c == '\t').ToArray()).Trim();
        if (content.Length == 0) return false;
        if (content.Length > 240) content = content[..240];
        var relay = RelayForInject();
        if (relay is null) return false;
        string? code = _catalog?.CodeForName("ChatChannelMessageRequest");   // kql->kqd, résolu du mapping streamé
        if (code is null) { TransportLog.Line("[chat] envoi impossible : ChatChannelMessageRequest non mappé."); return false; }
        byte[] pkt = MoveForge.BuildChatChannelMessageFramed(code, channel, content);
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        return ok;
    }
}
