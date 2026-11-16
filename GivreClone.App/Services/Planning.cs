using System;
using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>Une PLAGE horaire hebdomadaire : jours (bitmask bit0=Lundi..bit6=Dimanche) + heure de debut/fin en minutes
/// depuis minuit. Si EndMin &lt;= StartMin, la plage CHEVAUCHE minuit (ex 22:00-&gt;06:00).</summary>
public sealed class PlanningSlot
{
    // Identité STABLE de la plage (occurrences / budgets du scheduler de connexion) : ni l'index (décalé si on supprime une
    // autre plage) ni l'heure (modifiable pendant la plage). Absent des anciens fichiers -> généré au chargement, sauvé ensuite.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";   // libellé libre de la plage (ex « Pêcheur ») affiché sur le planning
    public string Script { get; set; } = ""; // NOM du script de la bibliothèque à lancer PENDANT cette plage (précis, pas de rotation)
    public int Days { get; set; }       // bitmask : 1=Lun 2=Mar 4=Mer 8=Jeu 16=Ven 32=Sam 64=Dim
    public int StartMin { get; set; }   // 0..1439
    public int EndMin { get; set; }     // 0..1439 ; <= StartMin => chevauche minuit
}

/// <summary>Un script NOMME de la bibliotheque du planning. Permet de PREVOIR PLUSIEURS scripts par personnage, chacun
/// ASSIGNE a une plage precise (voir <see cref="PlanningEval.SlotBody"/> et <see cref="PlanningSlot.Script"/>). Fige a
/// l'ajout (corps = texte de l'editeur Lua ou d'un fichier .lua chargé).</summary>
public sealed class PlanningScript
{
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";   // corps Lua fige
    // Fichier .lua d'origine (ajout depuis un fichier). Non vide -> le corps est RECHARGÉ depuis ce fichier au début de
    // chaque plage (le trajet édité sur disque est pris en compte sans ré-importer). Vide = script figé (éditeur).
    public string SourcePath { get; set; } = "";
}

/// <summary>Planning d'un personnage : quand faire tourner le script (plages), avec humanisation optionnelle. Le script
/// est FIGE ici (le champ d'edition Lua est partage cote UI) pour que l'auto-lancement soit deterministe.</summary>
public sealed class CharPlanning
{
    public bool Enabled { get; set; }
    public string Script { get; set; } = "";   // script Lua UNIQUE (repli/compat) a lancer si la bibliotheque est vide
    public int JitterMin { get; set; }          // humanisation : retard/avance aleatoire (0..N min) STABLE par jour aux bornes
    public List<PlanningSlot> Slots { get; set; } = new();
    public List<PlanningScript> Scripts { get; set; } = new();   // BIBLIOTHEQUE : le scheduler en tire un par plage/jour (variete). Vide -> Script unique.
    // « Vrai planning » (V1) : le compte est CONNECTÉ au début d'une plage (direct en jeu, dernier perso joué) et DÉCONNECTÉ
    // à la fin (après le combat en cours). N'agit que si Enabled. Le planning ne déconnecte que ce qu'il a connecté.
    public bool AutoConnect { get; set; }
    public string AccountId { get; set; } = "";   // Account.Id qui porte ce perso : appris quand le perso est vu en jeu
    // Dernier aperçu du perso en jeu (affichage de sa ligne « hors-ligne · géré par le planning » : icône de classe, niveau).
    public int? BreedIndex { get; set; }
    public int? GenderIndex { get; set; }
    public int? Level { get; set; }
}

/// <summary>Identité de session retenue par un compte à rebours : un PID peut être conservé ou réutilisé après une reconnexion.</summary>
public readonly record struct PlanningSession(string AccountId, int Pid, int LaunchGeneration, int SocketSession, string? Character);

/// <summary>Origine du script retenu par <see cref="PlanningEval.ResolveSlotScript"/>.</summary>
public enum SlotScriptSource
{
    None,       // rien à lancer (plage sans assignation, ni script figé, ni éditeur)
    Slot,       // script de la bibliothèque ASSIGNÉ à la plage
    Missing,    // plage assignée à un script INTROUVABLE (supprimé de la bibliothèque) : rien lancé, signalé
    Fallback,   // plage sans assignation : script unique figé (CharPlanning.Script)
    Editor,     // plage sans assignation, pas de script figé : texte de l'éditeur Lua du bot affiché
}

/// <summary>Script retenu pour une plage : corps à lancer (null = rien), origine, et nom assigné (Slot / Missing).</summary>
public readonly record struct SlotScriptPick(string? Body, SlotScriptSource Source, string? Name);

/// <summary>Action du scheduler pour un bot (<see cref="PlanningEval.DecideRun"/>).</summary>
public enum PlanRunAction
{
    None,           // rien à faire (plage déjà servie, script en cours laissé, pause manuelle, hors plage sans script)
    Start,          // rien ne tourne : lancer le script de la plage
    Switch,         // nouvelle plage, script DIFFÉRENT : arrêter celui en cours PUIS lancer celui de la nouvelle plage
    Keep,           // nouvelle plage, MÊME script (même corps) : il continue, désormais tenu par la nouvelle plage
    StopSlotEnd,    // hors plage : arrêter le script (fin de plage)
    StopNoScript,   // nouvelle plage SANS script à lancer (introuvable / aucun) : arrêter celui de la plage précédente
    NoScript,       // plage sans script à lancer, rien ne tourne : signaler (une fois par plage)
}

/// <summary>Éditions de la bibliothèque et des assignations plage -> script (pures, testées). Règle d'or : une assignation
/// ne change QUE sur un choix explicite (sélection d'un script, renommage propagé) — jamais sur un artefact d'affichage
/// (ComboBox dont la liste de choix est reconstruite), et jamais vidée en silence par la suppression d'un script.</summary>
public static class PlanningEdit
{
    /// <summary>Choix EXPLICITE du script d'une plage. Vide / blanc IGNORÉ : la liste n'a pas d'entrée « aucun », un vide
    /// vient forcément d'une ComboBox réinitialisée (source modifiée, conteneur démonté), jamais de l'utilisateur.
    /// Renvoie true si l'assignation a changé (-> à sauvegarder).</summary>
    public static bool AssignScript(PlanningSlot slot, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || slot.Script == name) return false;
        slot.Script = name;
        return true;
    }

    /// <summary>Retire EXPLICITEMENT le script d'une plage (bouton « Retirer » : seul moyen de désassigner, la liste n'a pas
    /// d'entrée « aucun »). La plage retombe sur le script de repli figé (<see cref="CharPlanning.Script"/>), jamais sur le
    /// modèle d'exemple. Renvoie true si l'assignation a changé (-> à sauvegarder).</summary>
    public static bool ClearScript(PlanningSlot slot)
    {
        if (string.IsNullOrEmpty(slot.Script)) return false;
        slot.Script = "";
        return true;
    }

    /// <summary>Nom LIBRE dans la bibliothèque : ajoute ′ tant qu'un AUTRE script porte déjà ce nom (l'assignation se fait
    /// par nom, un doublon la rendrait ambiguë). Réservé aux noms SAISIS par l'utilisateur (saisie, nom de fichier,
    /// renommage) : un tel nom peut RÉTABLIR une plage « script introuvable » qui le portait (voir <see cref="OrphanedSlots"/>).</summary>
    public static string UniqueScriptName(CharPlanning cp, string name, PlanningScript? except = null)
    {
        string nm = name;
        while (cp.Scripts.Exists(s => !ReferenceEquals(s, except) && s.Name == nm)) nm += "′";
        return nm;
    }

    /// <summary>Nom inutilisé PARTOUT : ni dans la bibliothèque, ni encore assigné à une plage (même « introuvable »).</summary>
    public static bool IsNameFree(CharPlanning cp, string name)
        => !cp.Scripts.Exists(s => s.Name == name) && !cp.Slots.Exists(s => s.Script == name);

    /// <summary>Nom GÉNÉRÉ (« Script N ») pour un script ajouté sans nom : libre dans la bibliothèque ET inutilisé par les
    /// plages — un nom automatique ne doit jamais reprendre en silence une plage « script introuvable » (script supprimé)
    /// qui portait ce nom. <paramref name="format"/> : libellé localisé de N (ex « Script {0} »).</summary>
    public static string DefaultScriptName(CharPlanning cp, Func<int, string> format)
    {
        int first = cp.Scripts.Count + 1;
        for (int n = first; n < first + 1000; n++)
        {
            string nm = format(n);
            if (IsNameFree(cp, nm)) return nm;
        }
        // Libellé sans {0} (traduction défaillante) : suffixe ′ jusqu'à un nom libre partout.
        string fb = format(first);
        while (!IsNameFree(cp, fb)) fb += "′";
        return fb;
    }

    /// <summary>Plages assignées à <paramref name="name"/> alors qu'aucun script de la bibliothèque ne porte ce nom (état
    /// « script introuvable »). Appelé AVANT d'ajouter / renommer un script sous ce nom : ces plages vont être RÉTABLIES
    /// (l'appelant le journalise, jamais en silence).</summary>
    public static List<PlanningSlot> OrphanedSlots(CharPlanning cp, string name)
    {
        var res = new List<PlanningSlot>();
        if (string.IsNullOrWhiteSpace(name) || cp.Scripts.Exists(s => s.Name == name)) return res;
        foreach (var s in cp.Slots) if (s.Script == name) res.Add(s);
        return res;
    }

    /// <summary>Renomme un script de la bibliothèque et PROPAGE le nouveau nom aux plages qui l'assignaient (l'assignation
    /// suit le script). Nom vide refusé (renvoie l'ancien), espaces rognés, doublon suffixé ′. Un ancien nom vide n'est
    /// jamais propagé (sinon toutes les plages SANS assignation se retrouveraient liées). Renvoie le nom retenu.</summary>
    public static string RenameScript(CharPlanning cp, PlanningScript script, string? wanted)
        => RenameScript(cp, script, wanted, out _);

    /// <summary><see cref="RenameScript(CharPlanning, PlanningScript, string?)"/> + plages « script introuvable » qui
    /// portaient DÉJÀ le nouveau nom et sont rétablies par ce renommage (<paramref name="relinked"/>, à journaliser).</summary>
    public static string RenameScript(CharPlanning cp, PlanningScript script, string? wanted, out List<PlanningSlot> relinked)
    {
        relinked = new List<PlanningSlot>();
        string old = script.Name ?? "";
        string nv = (wanted ?? "").Trim();
        if (nv.Length == 0 || nv == old) return old;
        nv = UniqueScriptName(cp, nv, script);
        if (nv == old) return old;
        relinked = OrphanedSlots(cp, nv);   // AVANT le renommage : nv n'est encore porté par aucun script
        script.Name = nv;
        if (!string.IsNullOrWhiteSpace(old))
            foreach (var s in cp.Slots) if (s.Script == old) s.Script = nv;
        return nv;
    }

    /// <summary>Retire un script de la bibliothèque. Les plages qui l'assignaient GARDENT son nom : état « script
    /// introuvable » affiché, rien lancé (<see cref="PlanningEval.MissingScript"/>) — jamais vidées en silence ; il suffit de
    /// rajouter un script de ce nom, d'en choisir un autre, ou de retirer l'assignation (<see cref="ClearScript"/>) pour les
    /// réparer.</summary>
    public static bool RemoveScript(CharPlanning cp, PlanningScript script) => cp.Scripts.Remove(script);
}

/// <summary>Fichier planning.json : un CharPlanning par nom de personnage (comme combat.json).</summary>
public sealed class PlanningFile
{
    public Dictionary<string, CharPlanning> Plannings { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Evaluation "suis-je dans une plage active MAINTENANT ?" avec humanisation deterministe-par-jour (pas de
/// flottement des bornes qui ferait clignoter le demarrage/arret). Le jitter RETRECIT la fenetre vers l'interieur
/// (demarre un peu apres l'heure ronde, s'arrete un peu avant) -> jamais d'inversion des bornes.</summary>
public static class PlanningEval
{
    /// <summary>Une liaison connue ne peut pas basculer vers un homonyme d'un autre compte. Un planning ancien non lié
    /// reste utilisable si le personnage observé est unique ; la connexion auto apprend alors sa première liaison.</summary>
    public static bool CanUseAccount(CharPlanning cp, string accountId)
        => !string.IsNullOrEmpty(accountId) && (string.IsNullOrEmpty(cp.AccountId) || cp.AccountId == accountId);

    /// <summary>Revalide une déconnexion au moment de l'action, sans dépendre de l'OccKey du dernier tick throttlé.
    /// Une plage devenue active, une édition du planning ou un remplacement de session annulent l'ancien décompte.</summary>
    public static bool CanDisconnectSession(PlanningSession expected, PlanningSession current,
        IEnumerable<KeyValuePair<string, CharPlanning>> plans, DateTime now)
    {
        if (expected != current || current.Pid <= 0 || string.IsNullOrWhiteSpace(current.AccountId)
            || string.IsNullOrWhiteSpace(current.Character)) return false;
        bool ownsCharacter = false;
        foreach (var (name, cp) in plans)
        {
            if (!cp.Enabled || !cp.AutoConnect || cp.AccountId != current.AccountId) continue;
            if (IsActiveNow(cp, now, name)) return false; // n'importe quel personnage attendu sur CE compte
            if (name == current.Character && HasUsableSlot(cp)) ownsCharacter = true;
        }
        return ownsCharacter;
    }

    public static bool IsActiveNow(CharPlanning cp, DateTime now, string? charName)
        => ActiveSlotIndex(cp, now, charName) >= 0;

    /// <summary>Index de la PREMIERE plage active maintenant, ou -1. Sert au scheduler pour lancer le script PRECIS de
    /// cette plage — <see cref="SlotBody"/>.</summary>
    public static int ActiveSlotIndex(CharPlanning cp, DateTime now, string? charName)
    {
        for (int i = 0; i < cp.Slots.Count; i++)
            if (ActiveStartDay(cp.Slots[i], now, cp.JitterMin, charName ?? "?", cp.Slots[i].Id) is not null) return i;
        return -1;
    }

    /// <summary>Identifiant de l'OCCURRENCE de plage active (« id de plage @ jour de début »), ou null hors plage. Change à
    /// chaque nouvelle plage (même contiguë à la précédente) et chaque jour pour une plage continue (24 h) : sert à ré-armer
    /// les budgets « par plage » du scheduler de connexion. Indépendant de l'INDEX de la plage (supprimer une autre plage)
    /// et de ses HEURES (les modifier pendant la plage ne doit pas lever un arrêt manuel ni ré-armer le budget).</summary>
    public static string? ActiveOccurrence(CharPlanning cp, DateTime now, string? charName)
    {
        for (int i = 0; i < cp.Slots.Count; i++)
            if (ActiveStartDay(cp.Slots[i], now, cp.JitterMin, charName ?? "?", cp.Slots[i].Id) is DateTime d)
            {
                var s = cp.Slots[i];
                string id = string.IsNullOrEmpty(s.Id) ? s.StartMin.ToString(System.Globalization.CultureInfo.InvariantCulture) : s.Id;
                return id + "@" + d.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            }
        return null;
    }

    /// <summary>Le planning a-t-il au moins une plage UTILISABLE (un jour coché) ? Sans plage, la connexion auto ne
    /// déconnecte jamais rien (sinon l'utilisateur se verrouillerait dehors en activant un planning vide).</summary>
    public static bool HasUsableSlot(CharPlanning cp)
    {
        foreach (var s in cp.Slots) if (IsUsableSlot(s)) return true;
        return false;
    }

    private static bool IsUsableSlot(PlanningSlot slot)
        => slot.Days is > 0 and <= 127
        && slot.StartMin is >= 0 and < 1440
        && slot.EndMin is >= 0 and < 1440;

    /// <summary>Corps Lua PRÉCIS assigné à la plage <paramref name="slotIdx"/> : on résout le NOM
    /// <see cref="PlanningSlot.Script"/> dans la bibliothèque <see cref="CharPlanning.Scripts"/>. Renvoie null si la plage
    /// n'a pas de script assigné, ou si le nom est introuvable / le corps vide (voir <see cref="ResolveSlotScript"/>).</summary>
    public static string? SlotBody(CharPlanning cp, int slotIdx)
    {
        if (slotIdx < 0 || slotIdx >= cp.Slots.Count) return null;
        string name = cp.Slots[slotIdx].Script;
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var sc in cp.Scripts)
            if (sc.Name == name) return string.IsNullOrWhiteSpace(sc.Body) ? null : sc.Body;
        return null;
    }

    /// <summary>Nom de script ASSIGNÉ à la plage mais ABSENT de la bibliothèque (supprimé), ou null (plage sans
    /// assignation, ou script présent). Une assignation n'est jamais effacée en silence : l'éditeur affiche « script
    /// introuvable » et le scheduler ne lance RIEN d'autre à la place.</summary>
    public static string? MissingScript(CharPlanning cp, PlanningSlot slot)
    {
        string name = slot.Script;
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var sc in cp.Scripts) if (sc.Name == name) return null;
        return name;
    }

    /// <summary>Script à lancer pendant la plage <paramref name="slotIdx"/> : décision UNIQUE du scheduler (début de plage, ou
    /// plage déjà en cours après une reconnexion). 1) plage assignée et script présent -> SON corps ; 2) assignée mais script
    /// introuvable (ou vide) -> RIEN (signalé : lancer un autre trajet à la place serait pire) ; 3) plage SANS assignation ->
    /// script unique figé <see cref="CharPlanning.Script"/> (compat), sinon <paramref name="editorScript"/> (éditeur du bot
    /// affiché, déjà filtré par l'appelant), sinon rien. <paramref name="templateScript"/> : MODÈLE d'exemple de l'éditeur
    /// Lua, jamais lancé par le planning — un script figé (ou un éditeur) qui n'est que ce modèle compte comme VIDE (couvre
    /// les plannings déjà sauvés avec le modèle figé).</summary>
    public static SlotScriptPick ResolveSlotScript(CharPlanning cp, int slotIdx, string? editorScript, string? templateScript = null)
    {
        if (slotIdx >= 0 && slotIdx < cp.Slots.Count && !string.IsNullOrWhiteSpace(cp.Slots[slotIdx].Script))
        {
            string name = cp.Slots[slotIdx].Script;
            return SlotBody(cp, slotIdx) is { } body
                ? new SlotScriptPick(body, SlotScriptSource.Slot, name)
                : new SlotScriptPick(null, SlotScriptSource.Missing, name);
        }
        if (!string.IsNullOrWhiteSpace(cp.Script) && !SameScript(cp.Script, templateScript))
            return new SlotScriptPick(cp.Script, SlotScriptSource.Fallback, null);
        if (!string.IsNullOrWhiteSpace(editorScript) && !SameScript(editorScript, templateScript))
            return new SlotScriptPick(editorScript, SlotScriptSource.Editor, null);
        return new SlotScriptPick(null, SlotScriptSource.None, null);
    }

    /// <summary>Deux textes Lua IDENTIQUES aux fins de ligne et aux blancs de bord près (un modèle recopié par l'éditeur
    /// peut revenir en CRLF). null / vide ne correspond à rien.</summary>
    public static bool SameScript(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a == b) return true;
        return string.Equals(a.Replace("\r\n", "\n").Trim(), b.Replace("\r\n", "\n").Trim(), StringComparison.Ordinal);
    }

    /// <summary>Décision du scheduler pour UN bot à un instant (pure : le <c>BotsViewModel.EvaluatePlanning</c> l'applique,
    /// les tests la rejouent minute par minute). <paramref name="servedOcc"/> = occurrence de plage déjà SERVIE par le
    /// planning pour ce bot (null = pas encore vue : démarrage en pleine plage, bot revenu, planning activé) ;
    /// <paramref name="occ"/> = occurrence active maintenant (<see cref="ActiveOccurrence"/>, null = hors plage) ;
    /// <paramref name="runningBody"/> = corps du script EN COURS ; <paramref name="paused"/> = arrêt manuel / blocage
    /// pendant la plage servie (sans effet sur une NOUVELLE plage) ; <paramref name="resumed"/> = le script en cours vient
    /// d'être REPRIS après une reconnexion ; <paramref name="slotBody"/> = corps à lancer pour la plage active (null = rien),
    /// évalué seulement si nécessaire (rechargement disque). <paramref name="started"/> : un script a déjà été démarré
    /// par le planning pour cette occurrence ; sa fin normale ne doit pas relancer ses effets à chaque tick.
    /// Règles : hors plage -> arrêt. Même plage -> on ne touche pas à un script qui tourne (lancé par le planning OU à la
    /// main : l'utilisateur garde la main jusqu'à la plage suivante) ; rien ne tourne -> lancer (sauf pause ou plage déjà démarrée). NOUVELLE plage
    /// (même contiguë : 09:00 fin de l'une = début de l'autre) -> le planning reprend la main : même corps -> il continue,
    /// corps différent -> arrêt PUIS lancement du nouveau, rien à lancer -> arrêt. Script repris sans plage servie connue
    /// -> traité comme une nouvelle plage (il peut dater d'une plage précédente).</summary>
    public static PlanRunAction DecideRun(string? servedOcc, string? occ, bool running, string? runningBody,
                                          bool paused, bool resumed, Func<string?> slotBody, bool started = false)
    {
        if (occ is null) return running ? PlanRunAction.StopSlotEnd : PlanRunAction.None;
        bool newOcc = servedOcc is not null && servedOcc != occ;
        if (running)
        {
            if (!newOcc && !resumed) return PlanRunAction.None;
            string? body = slotBody();
            if (body is null) return PlanRunAction.StopNoScript;
            return SameScript(body, runningBody)
                ? (newOcc || servedOcc is null ? PlanRunAction.Keep : PlanRunAction.None)
                : PlanRunAction.Switch;
        }
        if ((paused || (started && !resumed)) && !newOcc) return PlanRunAction.None;
        return slotBody() is null ? PlanRunAction.NoScript : PlanRunAction.Start;
    }

    /// <summary>Une NOUVELLE occurrence de plage commence-t-elle pour un bot déjà servi (bascule de plage, même contiguë) ?
    /// Lève la pause manuelle et le signalement « rien à lancer » de la plage précédente.</summary>
    public static bool IsNewOccurrence(string? servedOcc, string? occ) => servedOcc is not null && occ is not null && servedOcc != occ;

    /// <summary>Une plage démarre-t-elle dans les <paramref name="withinMin"/> prochaines minutes (jitter compris) ? Sert à
    /// ne pas déconnecter un compte entre deux plages rapprochées (évite un aller-retour déconnexion/reconnexion).</summary>
    public static bool StartsWithin(CharPlanning cp, DateTime now, string? charName, int withinMin)
    {
        for (int m = 1; m <= withinMin; m++)
            if (ActiveSlotIndex(cp, now.AddMinutes(m), charName) >= 0) return true;
        return false;
    }

    /// <summary>Prochain début de plage dans les 7 jours (résolution minute, jitter compris), ou null. Affichage seulement.</summary>
    public static DateTime? NextStart(CharPlanning cp, DateTime now, string? charName)
    {
        var t = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        bool prev = ActiveSlotIndex(cp, t, charName) >= 0;
        for (int m = 1; m <= 7 * 1440; m++)
        {
            var at = t.AddMinutes(m);
            bool on = ActiveSlotIndex(cp, at, charName) >= 0;
            if (on && !prev) return at;
            prev = on;
        }
        return null;
    }

    /// <summary>Deux plages se CHEVAUCHENT-elles (au moins un jour commun ET un instant commun) ? Gère le passage de minuit
    /// et le bord de semaine (dimanche soir -> lundi). Sert à empêcher/signaler deux plages simultanées.</summary>
    public static bool Overlaps(PlanningSlot a, PlanningSlot b)
    {
        foreach (var (s1, e1) in WeekIntervals(a))
            foreach (var (s2, e2) in WeekIntervals(b))
                if (s1 < e2 && s2 < e1) return true;
        return false;
    }

    // Développe une plage en intervalles [début,fin) en MINUTES ABSOLUES dans la semaine [0,10080), en coupant les
    // débordements de minuit / bord de semaine.
    private static List<(int s, int e)> WeekIntervals(PlanningSlot slot)
    {
        var res = new List<(int, int)>();
        if (!IsUsableSlot(slot)) return res;
        bool overnight = slot.EndMin <= slot.StartMin;
        for (int d = 0; d < 7; d++)
        {
            if ((slot.Days & (1 << d)) == 0) continue;
            int start = d * 1440 + slot.StartMin;
            int end = overnight ? d * 1440 + 1440 + slot.EndMin : d * 1440 + slot.EndMin;
            const int W = 7 * 1440;
            if (end <= W) res.Add((start, end));
            else { res.Add((start, W)); res.Add((0, end - W)); }   // enjambe dimanche->lundi
        }
        return res;
    }

    // Jour de DÉBUT de l'occurrence de la plage active à `now`, ou null. Raisonne en minutes écoulées DEPUIS LE DÉBUT de
    // l'occurrence (commencée aujourd'hui, ou la veille pour la partie après minuit d'une plage de nuit / 24 h) : le jitter
    // rétrécit [0, durée) vers l'intérieur SANS modulo 1440 -> un début décalé au-delà de minuit ne bascule plus la plage
    // sur le mauvais jour (bug de l'ancien calcul). Le décalage est tiré sur le jour de DÉBUT : stable toute l'occurrence.
    private static DateTime? ActiveStartDay(PlanningSlot s, DateTime now, int jitter, string name, string slotId)
    {
        if (!IsUsableSlot(s)) return null;
        int start = s.StartMin;
        int span = s.EndMin > start ? s.EndMin - start : 1440 - start + s.EndMin;   // EndMin <= StartMin : chevauche minuit (== : 24 h)
        int mins = now.Hour * 60 + now.Minute;
        for (int back = 0; back <= 1; back++)
        {
            var day = now.Date.AddDays(-back);
            int dow = ((int)day.DayOfWeek + 6) % 7;   // Lundi=0..Dimanche=6
            if ((s.Days & (1 << dow)) == 0) continue;
            int rel = mins + back * 1440 - start;     // minutes écoulées depuis le début de CETTE occurrence
            int from = 0, to = span;
            if (jitter > 0 && span > 2 * jitter + 2)   // retrecit la fenetre, stable par occurrence -> humanise sans clignoter
            {
                from = Off(name, slotId, day.DayOfYear, 1, jitter);        // demarre 0..jitter plus tard
                to = span - Off(name, slotId, day.DayOfYear, 2, jitter);   // finit 0..jitter plus tot
            }
            if (rel >= from && rel < to) return day;
        }
        return null;
    }

    // Decalage deterministe 0..jitter (FNV-1a sur nom+id stable de plage+jour+sel) : stable toute la journee, different par bot/plage.
    private static int Off(string name, string slotId, int day, int salt, int jitter)
        => (int)(Hash(name, slotId, day, salt) % (uint)(jitter + 1));

    // FNV-1a sur (nom, id stable de plage, jour, sel) : base commune deterministe du jitter des bornes (sel 1/2) ET du tirage de
    // script (sel 3). Pur calcul entier -> AOT-safe, reproductible, stable par jour.
    private static uint Hash(string name, string slotId, int day, int salt)
    {
        uint h = 2166136261u;
        foreach (char c in name) h = (h ^ c) * 16777619u;
        foreach (char c in slotId ?? "") h = (h ^ c) * 16777619u;
        h = (h ^ (uint)day) * 16777619u;
        h = (h ^ (uint)salt) * 16777619u;
        return h;
    }
}
