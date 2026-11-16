using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services;

/// <summary>Paramètres d'une « expédition » : une zone (liste de cartes), des filtres de groupes de monstres et des limites d'arrêt.
/// Chaque champ liste est du texte libre : le formulaire reste simple.</summary>
public sealed record ExpeditionProfile(
    string Name,
    string Maps,                 // « -15,39 ; 12345 ; 4,-20 » (une carte par ligne ou par « ; ») : coordonnées x,y ou id de carte
    int MinMonsters = 1,         // taille MINI d'un groupe attaqué (1..8)
    int MaxMonsters = 8,         // taille MAXI d'un groupe attaqué (1..8)
    string Mandatory = "",       // gid de monstres dont au moins un doit être dans le groupe
    string Forbidden = "",       // gid de monstres qui rendent un groupe interdit
    string Amounts = "",         // « gid:min-max » : nombre exigé de tel monstre dans le groupe
    int MaxFights = 0,           // 0 = illimité
    int MaxMinutes = 0,          // 0 = illimité
    int StopAtPodsPercent = 95,  // 0 = ne pas surveiller les pods
    long BankMap = 0,            // > 0 : pods pleins -> aller déposer à cette banque puis reprendre (au lieu de s'arrêter)
    int BankDoor = -1,           // cellule de la porte du bâtiment de la banque sur BankMap (-1 = banquier sur la carte même)
    long BankInside = 0,         // carte atteinte par cette porte (intérieur de la banque)
    HdvPlan? Hdv = null,         // pods pleins : vendre d'abord à l'HDV (liste de remise en vente), puis banque
    int AnomalySubarea = 0,      // > 0 : farm d'une anomalie -> arrêt dès qu'elle se ferme (bonus perdu)
    ElixirPlan? Elixir = null,   // élixir uchronique gardé actif (fragments d'anomalie), racheté à l'HDV consommables
    bool ContinueAfterDeath = false, // défaite : continuer l'expédition au lieu de s'arrêter
    bool Gather = false,         // récolter sur chaque carte parcourue (gatherAll)
    bool Fight = true,           // combattre les groupes éligibles sur chaque carte parcourue
    long GatherItemId = 0,       // > 0 : ne récolter que cette ressource (ITEMS_TO_GATHER) ; 0 = tout ce que le métier permet
    int LoopStartIndex = 1,      // les cartes d'approche ne sont parcourues qu'au premier tour
    IReadOnlyList<long>? ActivityMaps = null, // cartes de la zone sélectionnée ; null = toutes les cartes du parcours
    int MaxConsecutiveDefeats = 3); // 0 = ne jamais arrêter sur une série de défaites ; défaut = 3

/// <summary>Élixir uchronique gardé actif pendant l'expédition : objet bu, altération qu'il donne, HDV consommables où en racheter
/// (0 = jamais), prix maximum d'UN élixir (0 = ne pas acheter), nombre acheté d'un coup (dès 10 : lots de 10 s'ils reviennent
/// moins cher à l'unité).</summary>
public sealed record ElixirPlan(long ItemGid, int AlterationId, long HdvMap = 0, long MaxPrice = 0, int Stock = 2);

/// <summary>Élixirs uchroniques (données du client) : sans l'altération, les monstres d'une anomalie ne donnent pas de fragments.
/// Boire un élixir retire les deux autres ; le jeu refuse d'en boire un dont l'altération est déjà active. L'altération disparaît
/// d'elle-même après ses combats.</summary>
public static class UchronicElixirs
{
    /// <summary>Un élixir : rang, objet, altération donnée, niveau requis, combats que dure l'altération (infobulle du jeu).</summary>
    public sealed record Tier(int Rank, long Gid, int AlterationId, int Level, int Fights);
    public static readonly IReadOnlyList<Tier> All = new Tier[]
    {
        new(1, 20962, 50, 60, 15),    // mineur : 15 combats
        new(2, 26871, 51, 120, 10),   // amélioré : 10 combats
        new(3, 26872, 52, 180, 5),    // majeur : 5 combats
    };
    /// <summary>Valeur d'expiration lisible comme « combats restants » pour cet élixir (sinon : date ou type inconnu).</summary>
    public static bool IsFightCount(Tier t, long expirationValue) => expirationValue > 0 && expirationValue <= t.Fights;
    /// <summary>Fragment d'anomalie (ressource lâchée par les monstres d'une anomalie sous élixir).</summary>
    public const long FragmentGid = 26870;
    public static Tier? ByRank(int rank) => All.FirstOrDefault(t => t.Rank == rank);
    public static Tier? ByAlteration(int id) => All.FirstOrDefault(t => t.AlterationId == id);
}

/// <summary>Paramètres d'un DONJON : salles (ordre du jeu), entrée, clés, nombre de passages.</summary>
public sealed record DungeonProfile(
    string Name,
    IReadOnlyList<long> Rooms,   // cartes des salles, dans l'ordre du jeu (la dernière = salle du boss)
    long EntranceMap,            // carte d'entrée (porte / PNJ du donjon)
    IReadOnlyList<(long Gid, int Qty, string Name)> Keys,
    int Runs = 1,                // nombre de passages (0 = tant qu'il reste des clés)
    int MaxMinutes = 0,          // 0 = illimité
    long BankMap = 0,            // > 0 : pods pleins entre deux passages -> banque
    int BankDoor = -1,
    int StopAtPodsPercent = 95,
    long BankInside = 0,
    ElixirPlan? Elixir = null,   // élixir uchronique gardé actif (bu dehors, entre deux passages)
    bool ContinueAfterDeath = false); // défaite : nouveau passage au lieu de s'arrêter

/// <summary>Dernière sélection de l'onglet Expédition, sérialisée en une ligne (« zone|sous-zone|min|max|obligatoires|interdits|combats|minutes|pods »)
/// pour tenir dans les réglages sans nouveau format de fichier.</summary>
public sealed record ExpeditionState(int AreaId, int SubAreaId, int Min, int Max, IReadOnlyList<int> Mandatory, IReadOnlyList<int> Forbidden, int Fights, int Minutes, int Pods,
                                     bool Bank = false, int DungeonId = 0, int Runs = 1, bool DungeonMode = false, bool Hdv = false,
                                     bool ContinueAfterDeath = false, bool Gather = false, bool Fight = true, long GatherItemId = 0,
                                     int MaxConsecutiveDefeats = 3)
{
    // 9 champs d'origine puis les ajouts (banque, donjon, HDV, reprise après mort) : une ligne d'une version précédente reste lisible.
    public string Serialize() => string.Join('|', AreaId, SubAreaId, Min, Max, string.Join(',', Mandatory), string.Join(',', Forbidden), Fights, Minutes, Pods,
                                              Bank ? 1 : 0, DungeonId, Runs, DungeonMode ? 1 : 0, Hdv ? 1 : 0, ContinueAfterDeath ? 1 : 0,
                                              Gather ? 1 : 0, Fight ? 1 : 0, GatherItemId, MaxConsecutiveDefeats);

    public static ExpeditionState? Parse(string? s)
    {
        var p = (s ?? "").Split('|');
        if (p.Length is not (9 or 13 or 14 or 15 or 18 or 19)) return null;
        static bool I(string t, out int v) => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        static List<int> L(string t) => t.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToList();
        if (!I(p[0], out int a) || !I(p[1], out int sub) || !I(p[2], out int mn) || !I(p[3], out int mx) || !I(p[6], out int f) || !I(p[7], out int m) || !I(p[8], out int pods)) return null;
        var st = new ExpeditionState(a, sub, Math.Clamp(mn, 1, 8), Math.Clamp(mx, 1, 8), L(p[4]), L(p[5]), Math.Max(0, f), Math.Max(0, m), Math.Clamp(pods, 0, 100));
        if (p.Length >= 13 && I(p[9], out int bank) && I(p[10], out int dj) && I(p[11], out int runs) && I(p[12], out int dm))
            st = st with { Bank = bank != 0, DungeonId = Math.Max(0, dj), Runs = Math.Max(0, runs), DungeonMode = dm != 0 };
        if (p.Length >= 14 && I(p[13], out int hdv)) st = st with { Hdv = hdv != 0 };
        if (p.Length >= 15 && I(p[14], out int revive)) st = st with { ContinueAfterDeath = revive != 0 };
        if (p.Length >= 18 && I(p[15], out int gather) && I(p[16], out int fightOn)
            && long.TryParse(p[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out long item))
            st = st with { Gather = gather != 0, Fight = fightOn != 0, GatherItemId = Math.Max(0, item) };
        if (p.Length >= 19 && I(p[18], out int maxDefeats)) st = st with { MaxConsecutiveDefeats = Math.Max(0, maxDefeats) };
        return st;
    }
}

/// <summary>Génère le script Lua d'une expédition à partir du profil. Pur (aucune dépendance UI) : testable.
/// Le script n'utilise que l'API existante : config:set*Monsters, travelTo, fight, map:forceFight, getCountFight, getPodsPercent.</summary>
public static class ExpeditionScript
{
    private static readonly Regex CoordRx = new(@"^-?\d+\s*,\s*-?\d+$", RegexOptions.Compiled);
    private static readonly char[] ListSeparators = { ',', ';', '\n', '\r', '\t', ' ' };

    /// <summary>Cartes valides sous forme de littéral Lua (« "x,y" » ou id numérique). Les lignes illisibles vont dans <paramref name="ignored"/>.</summary>
    public static List<string> ParseMaps(string? text, List<string>? ignored = null)
    {
        var res = new List<string>();
        foreach (var raw in (text ?? "").Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = raw.Trim();
            if (t.Length == 0) continue;
            if (CoordRx.IsMatch(t)) res.Add("\"" + Regex.Replace(t, @"\s+", "") + "\"");
            else if (long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0) res.Add(id.ToString(CultureInfo.InvariantCulture));
            else ignored?.Add(t);
        }
        return res;
    }

    /// <summary>Entiers positifs d'une liste libre (gid) ; les jetons non numériques sont ignorés (et listés).</summary>
    public static List<long> ParseIds(string? text, List<string>? ignored = null)
    {
        var res = new List<long>();
        foreach (var t in (text ?? "").Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out long v) && v > 0) { if (!res.Contains(v)) res.Add(v); }
            else ignored?.Add(t);
        }
        return res;
    }

    /// <summary>« gid:min-max » (ou « gid:n » = exactement n) -> (gid, min, max) bornés 0..8.</summary>
    public static List<(long Gid, int Min, int Max)> ParseAmounts(string? text, List<string>? ignored = null)
    {
        var res = new List<(long, int, int)>();
        foreach (var t in (text ?? "").Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(t, @"^(\d+):(\d+)(?:-(\d+))?$");
            if (!m.Success || !long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long gid) || gid <= 0) { ignored?.Add(t); continue; }
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int a)
                || (m.Groups[3].Success && !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            { ignored?.Add(t); continue; }
            a = Math.Clamp(a, 0, 8);
            int b = m.Groups[3].Success ? Math.Clamp(int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), 0, 8) : a;
            res.Add((gid, Math.Min(a, b), Math.Max(a, b)));
        }
        return res;
    }

    /// <summary>Ordre de visite d'une zone : plus proche voisin (distance de Manhattan) depuis <paramref name="start"/> ; les cartes sans
    /// coordonnées passent en dernier, dans leur ordre d'origine. Évite les allers-retours quand on enchaîne toutes les cartes d'une sous-zone.</summary>
    public static List<long> NearestOrder(IReadOnlyList<(long Id, (int X, int Y)? P)> maps, (int X, int Y)? start)
    {
        var left = maps.Where(m => m.P is not null).ToList();
        var res = new List<long>(maps.Count);
        var cur = start ?? (left.Count > 0 ? left[0].P!.Value : (0, 0));
        while (left.Count > 0)
        {
            int best = 0;
            long bd = long.MaxValue;
            for (int i = 0; i < left.Count; i++)
            {
                var q = left[i].P!.Value;
                long d = Math.Abs((long)q.X - cur.X) + Math.Abs((long)q.Y - cur.Y);
                if (d < bd) { bd = d; best = i; }
            }
            res.Add(left[best].Id);
            cur = left[best].P!.Value;
            left.RemoveAt(best);
        }
        res.AddRange(maps.Where(m => m.P is null).Select(m => m.Id));
        return res;
    }

    /// <summary>Déplie les chemins du graphe existant, y compris le retour en boucle, sans supprimer les cartes revisitées.
    /// Les trajets sans chemin sont signalés sans retirer leur destination : travelTo peut encore utiliser un zaap.</summary>
    public static List<long> TraversedMaps(IReadOnlyList<long> maps, long start, Func<long, long, IReadOnlyList<long>> path,
        List<(long From, long To)>? unreachableLegs = null)
    {
        var result = new List<long>();
        if (maps.Count == 0) return result;
        long current = start > 0 ? start : maps[0];
        result.Add(current);
        foreach (long target in maps.Append(maps[0]))
        {
            if (target == current) continue;
            var arrivals = path(current, target);
            if (arrivals.Count == 0) unreachableLegs?.Add((current, target));
            foreach (long arrival in arrivals)
                if (result[^1] != arrival) result.Add(arrival);
            if (result[^1] != target) result.Add(target);
            current = target;
        }
        if (result.Count > 1 && result[^1] == maps[0] && result.Take(result.Count - 1).Contains(maps[0]))
            result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>Cartes VISITABLES d'une sous-zone : la table du jeu y range aussi les intérieurs (maisons, grottes, sous-sols) qui
    /// partagent les coordonnées de l'extérieur ou n'en ont pas, et des cartes d'un autre calque. Garde les cartes à coordonnées du
    /// calque renseigné majoritaire (worldMap négatif = intérieur/hors atlas), une seule par coordonnée.
    /// Sans calque renseigné, conserver le repli historique. Sans aucune coordonnée connue : liste
    /// d'origine (table des positions absente -> on ne filtre rien plutôt que tout).
    /// Avec un graphe de navigation disponible, écarte aussi les positions factices 0,0 sans nœud de navigation.
    /// Le score départage les cartes d'une même coordonnée ET du même calque (extérieur avant intérieur).</summary>
    public static List<long> CleanMaps(IReadOnlyList<(long Id, (int X, int Y)? P, int? World)> maps,
        Func<long, bool?>? knownNavigationMap = null, Func<long, int>? walkScore = null)
    {
        // Les instances de combat d'Incarnam/Astrub portent aussi un calque d'atlas,
        // mais une position factice 0,0 et aucune transition de navigation. Filtrer
        // AVANT la déduplication : sinon l'instance masque une vraie carte en 0,0.
        // Un graphe absent ne constitue pas une preuve d'absence de transition.
        // Une carte hors atlas (calque < 0 : intérieur, donjon) explicitement absente du graphe n'a aucun repli de voyage :
        // la garder lançait une expédition vouée à no_route (live 06/11, Dojo du Vent 950). Les cartes d'atlas hors graphe
        // gardent le repli voyage par coordonnées.
        var candidatesWithNavigation = maps.Where(m => knownNavigationMap?.Invoke(m.Id) != false
            || m.P != (0, 0) && m.World is >= 0).ToList();
        var located = candidatesWithNavigation.Where(m => m.P is not null).ToList();
        if (located.Count == 0) return candidatesWithNavigation.Select(m => m.Id).Distinct().ToList();
        // Astrub contains more interior maps (worldMap=-1) than exterior maps.
        // Negative/unknown layers must not win the vote when an atlas layer exists.
        var onAtlas = located.Where(m => m.World is >= 0).ToList();
        var candidates = onAtlas.Count > 0 ? onAtlas : located;
        int? world = candidates.GroupBy(m => m.World).OrderByDescending(g => g.Count()).First().Key;
        var chosen = new Dictionary<(int, int), (long Id, int Score, int Index)>();
        var res = new List<long>();
        foreach (var m in located)
        {
            if (m.World != world) continue;
            int score = walkScore?.Invoke(m.Id) ?? 0;
            if (!chosen.TryGetValue(m.P!.Value, out var prior))
            {
                chosen[m.P.Value] = (m.Id, score, res.Count);
                res.Add(m.Id);
            }
            else if (score > prior.Score)
            {
                // Un intérieur et son extérieur peuvent partager AUSSI le même calque.
                // Les sorties de bord priment ; une égalité préserve l'ordre historique.
                res[prior.Index] = m.Id;
                chosen[m.P.Value] = (m.Id, score, prior.Index);
            }
        }
        return res;
    }

    // Retour banque partagé (expédition et donjon) : MAX_PODS + bank() déclarative (voir RoadDesign.BankFunction).
    private static void AppendBank(StringBuilder sb, long bankMap, int bankDoor, long insideMap, int maxPods, HdvPlan? hdv = null)
    {
        sb.AppendLine($"MAX_PODS = {Math.Clamp(maxPods <= 0 ? 90 : maxPods, 10, 100)}");
        sb.AppendLine();
        sb.Append(RoadDesign.BankFunction(bankMap, bankDoor, insideMap, hdv));
        sb.AppendLine();
    }

    /// <summary>ensureElixir() : appelé à chaque carte. Altération absente -> boit un élixir du sac, après en avoir acheté à l'HDV
    /// consommables s'il n'y en a plus (prix maximum obligatoire). Échec : nouvel essai quelques cartes plus loin, sans boucler.</summary>
    private static void AppendElixir(StringBuilder sb, ElixirPlan e)
    {
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        sb.AppendLine($"local ELIXIR_GID, ELIXIR_ALT = {I(e.ItemGid)}, {e.AlterationId}");
        sb.AppendLine($"local ELIXIR_HDV, ELIXIR_MAX, ELIXIR_STOCK = {I(Math.Max(0, e.HdvMap))}, {I(Math.Max(0, e.MaxPrice))}, {Math.Clamp(e.Stock, 1, 50)}");
        sb.AppendLine("local elixirWait = 0   -- cartes à attendre avant un nouvel essai (après un échec)");
        sb.AppendLine("local function ensureElixir()");
        sb.AppendLine("    if hasAlteration(ELIXIR_ALT) == true then return end");
        sb.AppendLine("    if elixirWait > 0 then elixirWait = elixirWait - 1 return end");
        sb.AppendLine("    if getInventoryItemCount(ELIXIR_GID) == 0 and ELIXIR_HDV > 0 and ELIXIR_MAX > 0 then");
        sb.AppendLine("        printMessage('Élixir uchronique : achat (HDV consommables)')");
        sb.AppendLine("        if goToHdvBuy(ELIXIR_HDV) then");
        sb.AppendLine("            local left = ELIXIR_STOCK");
        sb.AppendLine("            if left >= 10 then   -- lots de 10 s'ils reviennent moins cher à l'unité (plafond : 10 x le prix max)");
        sb.AppendLine("                local p1, p10 = sale:GetPriceItem(ELIXIR_GID, 1), sale:GetPriceItem(ELIXIR_GID, 10)");
        sb.AppendLine("                while left >= 10 and p10 > 0 and p10 <= ELIXIR_MAX * 10 and (p1 <= 0 or p10 < p1 * 10) do");
        sb.AppendLine("                    if not sale:buyItem(ELIXIR_GID, 10, ELIXIR_MAX * 10) then break end");
        sb.AppendLine("                    left = left - 10");
        sb.AppendLine("                    delay(1000)");
        sb.AppendLine("                end");
        sb.AppendLine("            end");
        sb.AppendLine("            for _ = 1, left do");
        sb.AppendLine("                if not sale:buyItem(ELIXIR_GID, 1, ELIXIR_MAX) then break end");
        sb.AppendLine("                delay(1000)");
        sb.AppendLine("            end");
        sb.AppendLine("            leaveDialog()");
        sb.AppendLine("        end");
        sb.AppendLine("    end");
        sb.AppendLine("    if getInventoryItemCount(ELIXIR_GID) == 0 then");
        sb.AppendLine("        printWarning('Élixir uchronique : aucun dans le sac (achat impossible ou au-dessus du prix maximum)')");
        sb.AppendLine("        elixirWait = 10");
        sb.AppendLine("        return");
        sb.AppendLine("    end");
        sb.AppendLine("    useItem(ELIXIR_GID, 1)");
        sb.AppendLine("    delay(1500)");
        sb.AppendLine("    if hasAlteration(ELIXIR_ALT) == true then printSuccess('Élixir uchronique actif') else elixirWait = 5 end");
        sb.AppendLine("end");
        sb.AppendLine();
    }

    /// <summary>Réaction à une défaite confirmée (combat:lastFightResult().lost). Arrêt par défaut ; sinon le script continue
    /// directement (le jeu n'a plus de Phénix : le perso repart tout de suite après sa mort). DEFEATS = défaites d'affilée (une victoire remet à zéro), affiché en console.</summary>
    private static void AppendDefeatPolicy(StringBuilder sb, string label, bool continueAfterDeath, int maxConsecutiveDefeats = 0)
    {
        sb.AppendLine($"local CONTINUE_AFTER_DEATH = {(continueAfterDeath ? "true" : "false")}");
        sb.AppendLine("DEFEATS = 0");
        sb.AppendLine("function onDefeat()");
        sb.AppendLine($"    if not CONTINUE_AFTER_DEATH then printError('{label} : défaite, arrêt') return false end");
        sb.AppendLine("    DEFEATS = DEFEATS + 1");
        if (maxConsecutiveDefeats > 0)
            sb.AppendLine($"    if DEFEATS >= {maxConsecutiveDefeats.ToString(CultureInfo.InvariantCulture)} then printError('{label} : reason=defeat_streak, défaites consécutives=' .. DEFEATS .. ', arrêt') return false end");
        sb.AppendLine($"    printWarning('{label} : défaite (' .. DEFEATS .. ' d\\'affilée), on continue')");
        // Respiration APRES une mort : le personnage reapparait au zaap, le script repart aussitot en trajet puis en
        // combat. Shadowbot est mort 4 fois en 3 minutes (12:55 -> 12:58) puis 2 fois avant que le serveur ferme sa session
        // et pose un ban temporaire de 5 min (13:06). Le palier croit avec les defaites consecutives, puis se stabilise.
        sb.AppendLine("    local rest = 3000 + math.min(DEFEATS, 4) * 3000");
        sb.AppendLine($"    printMessage('{label} : pause de ' .. math.floor(rest / 1000) .. ' s apres la mort (anti-bannissement)')");
        sb.AppendLine("    if type(delay) == 'function' then delay(rest) end");
        sb.AppendLine("    return true");
        sb.AppendLine("end");
    }

    public static string Build(ExpeditionProfile p, List<string>? warnings = null)
    {
        var bad = new List<string>();
        var maps = ParseMaps(p.Maps, bad);
        var mandatory = ParseIds(p.Mandatory, bad);
        var forbidden = ParseIds(p.Forbidden, bad);
        var amounts = ParseAmounts(p.Amounts, bad);
        int min = Math.Clamp(p.MinMonsters, 1, 8), max = Math.Clamp(p.MaxMonsters, 1, 8);
        if (min > max) (min, max) = (max, min);
        foreach (var g in mandatory.Intersect(forbidden)) warnings?.Add($"monstre {g} à la fois obligatoire et interdit : aucun groupe ne sera éligible");
        if (maps.Count == 0) warnings?.Add("aucune carte valide dans la zone");
        foreach (var b in bad) warnings?.Add($"« {b} » ignoré (format non reconnu)");

        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        static string Ids(IEnumerable<long> xs) => string.Join(", ", xs.Select(I));
        string name = (p.Name ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (name.Length == 0) name = "Sans nom";

        var sb = new StringBuilder();
        sb.AppendLine($"-- Expédition « {name} » : script généré, modifiable à la main.");
        sb.AppendLine($"config:setMinMonsters({min})");
        sb.AppendLine($"config:setMaxMonsters({max})");
        sb.AppendLine($"config:setMandatoryMonsters({{ {Ids(mandatory)} }})");
        sb.AppendLine($"config:setForbiddenMonsters({{ {Ids(forbidden)} }})");
        sb.AppendLine("config:setAmountOfSpecificMonsters({ " + string.Join(", ", amounts.Select(a => $"{{ {I(a.Gid)}, {a.Min}, {a.Max} }}")) + " })");
        sb.AppendLine();
        sb.AppendLine($"local MAPS = {{ {string.Join(", ", maps)} }}");
        if (p.ActivityMaps is { } activityMaps)
            sb.AppendLine($"local ACTIVITY_MAPS = {{ {string.Join(", ", activityMaps.Distinct().Select(id => $"[{I(id)}] = true"))} }}");
        sb.AppendLine($"local GATHER = {(p.Gather ? "true" : "false")}");
        sb.AppendLine($"local FIGHT = {(p.Fight ? "true" : "false")}");
        if (p.Gather && p.GatherItemId > 0) sb.AppendLine($"ITEMS_TO_GATHER = {{ {I(p.GatherItemId)} }}");
        sb.AppendLine($"local MAX_FIGHTS = {Math.Max(0, p.MaxFights)}");
        sb.AppendLine($"local MAX_MINUTES = {Math.Max(0, p.MaxMinutes)}");
        // Banque (ou HDV) configurée : les pods pleins déclenchent bank() (boucle du moteur) au lieu d'arrêter l'expédition.
        bool withHdv = p.Hdv is { Map: > 0 } h && h.Rules.Count > 0;
        if (p.Gather && p.BankMap <= 0 && !withHdv)
            sb.AppendLine($"MAX_PODS = {(p.StopAtPodsPercent > 0 ? Math.Clamp(p.StopAtPodsPercent, 1, 100) : 100)}");
        sb.AppendLine($"local STOP_PODS = {(p.BankMap > 0 || withHdv ? 0 : Math.Clamp(p.StopAtPodsPercent, 0, 100))}");
        sb.AppendLine("local idx = 0");
        sb.AppendLine("local unreachable = {}");
        sb.AppendLine("local travelFailures = {}");
        sb.AppendLine("local function allActivityMapsUnreachable()");
        sb.AppendLine(p.ActivityMaps is not null
            ? "    for target in pairs(ACTIVITY_MAPS) do if not unreachable[target] then return false end end"
            : "    for _, target in ipairs(MAPS) do if not unreachable[target] then return false end end");
        sb.AppendLine("    return true");
        sb.AppendLine("end");
        sb.AppendLine("local okTime, startedAt = pcall(os.time)");
        sb.AppendLine();
        sb.AppendLine("local function lastFightLost()");
        sb.AppendLine("    local r = combat and combat.lastFightResult and combat:lastFightResult()");
        sb.AppendLine("    return r and r.lost == true");
        sb.AppendLine("end");
        AppendDefeatPolicy(sb, "Expédition", p.ContinueAfterDeath, p.MaxConsecutiveDefeats);
        sb.AppendLine("local ECO_START_TIME = (okTime and startedAt) or 0");
        sb.AppendLine("local ECO_START_KAMAS = (type(getKamas) == 'function' and getKamas()) or -1");
        sb.AppendLine("local ECO_START_XP = (type(getCharacterXp) == 'function' and getCharacterXp()) or -1");
        sb.AppendLine("local ECO_LAST = ECO_START_TIME");
        sb.AppendLine("if (ECO_START_KAMAS < 0 or ECO_START_XP < 0) and type(printWarning) == 'function' then printWarning('[economie] kamas ou XP inconnus au départ : mesure partielle') end");
        sb.AppendLine("local function ecoReport()");
        sb.AppendLine("    local now = (okTime and os.time()) or 0");
        sb.AppendLine("    if now - ECO_LAST < 1800 then return end");
        sb.AppendLine("    ECO_LAST = now");
        sb.AppendLine("    local h = math.max(1, now - ECO_START_TIME) / 3600");
        sb.AppendLine("    local k = (type(getKamas) == 'function' and getKamas()) or -1");
        sb.AppendLine("    local x = (type(getCharacterXp) == 'function' and getCharacterXp()) or -1");
        sb.AppendLine("    local kh = (k >= 0 and ECO_START_KAMAS >= 0) and math.floor((k - ECO_START_KAMAS) / h) or -1");
        sb.AppendLine("    local xh = (x >= 0 and ECO_START_XP >= 0) and math.floor((x - ECO_START_XP) / h) or -1");
        sb.AppendLine("    print('[economie] kamas/h=' .. kh .. ' xp/h=' .. xh)");
        sb.AppendLine("end");
        sb.AppendLine("local function limitReached()");
        sb.AppendLine("    ecoReport()");
        sb.AppendLine("    if MAX_FIGHTS > 0 and getCountFight() >= MAX_FIGHTS then printSuccess('Expédition : ' .. MAX_FIGHTS .. ' combats atteints') return true end");
        sb.AppendLine("    if MAX_MINUTES > 0 and okTime and os.time() - startedAt >= MAX_MINUTES * 60 then printSuccess('Expédition : ' .. MAX_MINUTES .. ' min écoulées') return true end");
        sb.AppendLine("    if STOP_PODS > 0 and getPodsPercent() >= STOP_PODS then printWarning('Expédition : pods à ' .. getPodsPercent() .. '%, arrêt') return true end");
        if (p.AnomalySubarea > 0)
            sb.AppendLine($"    if anomaly and not anomaly:isOpen({p.AnomalySubarea}) then printSuccess('Expédition : anomalie fermée, arrêt') return true end");
        sb.AppendLine("    return false");
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("local function attempt(action, target)");
        sb.AppendLine("    local reason, failureDetail, origin");
        sb.AppendLine("    for n = 1, 3 do");
        sb.AppendLine("        local ok, status, detail = action(target, origin)");
        sb.AppendLine("        if ok then return true end");
        sb.AppendLine("        reason = status or 'action_failed'");
        sb.AppendLine("        if action == fight then origin = detail else failureDetail = detail end");
        sb.AppendLine("        if action == fight and reason == 'no_matching_group' then return false, reason end");
        sb.AppendLine("    end");
        sb.AppendLine("    if action ~= travelTo or reason ~= 'no_route' then");
        sb.AppendLine("        printError('Expédition : action=' .. (action == fight and 'fight' or 'travel') .. ' map=' .. tostring(MAPS[idx]) .. ' attempts=3 reason=' .. reason .. (failureDetail and ' detail=' .. failureDetail or ''))");
        sb.AppendLine("    end");
        sb.AppendLine("    return false, reason");
        sb.AppendLine("end");
        if (p.BankMap > 0 || withHdv) AppendBank(sb, p.BankMap, p.BankDoor, p.BankInside, p.StopAtPodsPercent, p.Hdv);
        if (p.Elixir is { ItemGid: > 0, AlterationId: > 0 } elixir) AppendElixir(sb, elixir);
        sb.AppendLine("function move()");
        if (withHdv) sb.AppendLine("    HDV_DONE = false   -- pods redescendus : prochain passage à l'HDV autorisé");
        sb.AppendLine("    if GATHER and type(gatherAll) ~= 'function' then printError('Expédition : API de récolte indisponible') return false end");
        sb.AppendLine("    if #MAPS == 0 then printError('Expédition : zone vide') return false end");
        sb.AppendLine("    if limitReached() then return false end");
        if (p.Elixir is { ItemGid: > 0, AlterationId: > 0 }) sb.AppendLine("    ensureElixir()");
        if (p.LoopStartIndex > 1)
            sb.AppendLine($"    if idx == #MAPS then idx = {Math.Clamp(p.LoopStartIndex, 1, Math.Max(1, maps.Count)) - 1} end");
        sb.AppendLine("    idx = idx % #MAPS + 1");
        sb.AppendLine("    if unreachable[MAPS[idx]] then return true end");
        sb.AppendLine("    local arrived, reason = attempt(travelTo, MAPS[idx])");
        sb.AppendLine("    if not arrived then");
        // Un trajet qui echoue pour une autre raison que 'no_route' ne termine PLUS l'expedition : Shadowbot s'est retrouve
        // bloque sur la carte 209192199, dont AUCUNE cellule de transition n'est decodee (« mapChangeData absent ? » sur
        // les 4 directions), et le script s'est arrete a 12:38:27 apres 3 essais. On tente la sortie par havre-sac (qui
        // reinitialise la position hors de la carte piege), puis on passe a la carte suivante.
        // travelTo ne renvoie qu'un booleen : reason vaut toujours 'action_failed' et la branche no_route ne se declenchait
        // jamais. Shadowbot a retente 14 fois chacune deux cartes sans chemin world-graph (10/11) : apres 2 echecs, carte ignoree.
        sb.AppendLine("        travelFailures[MAPS[idx]] = (travelFailures[MAPS[idx]] or 0) + 1");
        sb.AppendLine("        if reason ~= 'no_route' and travelFailures[MAPS[idx]] < 2 then");
        sb.AppendLine("            printWarning('Expedition : trajet impossible (reason=' .. tostring(reason) .. '), tentative de sortie puis carte suivante')");
        sb.AppendLine("            if type(map) == 'table' and type(map.exitHavenBag) == 'function' then pcall(map.exitHavenBag) end");
        sb.AppendLine("            return true");
        sb.AppendLine("        end");
        sb.AppendLine("        unreachable[MAPS[idx]] = true");
        sb.AppendLine("        if type(map) == 'table' and type(map.exitHavenBag) == 'function' then pcall(map.exitHavenBag) end");
        sb.AppendLine("        printWarning('Expédition : map=' .. tostring(MAPS[idx]) .. ' reason=' .. tostring(reason) .. ', carte ignorée pour cette expédition')");
        sb.AppendLine("        if allActivityMapsUnreachable() then printError('Expédition : reason=all_activity_maps_unreachable, toutes les cartes de la zone sont inaccessibles, arrêt') return false end");
        sb.AppendLine("        return true");
        sb.AppendLine("    end");
        sb.AppendLine("    printMessage('[expedition-route] ' .. idx .. '/' .. #MAPS .. ' map=' .. tostring(MAPS[idx]))");
        if (p.ActivityMaps is not null)
            sb.AppendLine("    if not ACTIVITY_MAPS[MAPS[idx]] then return true end");
        // Récolte d'abord : les pods pleins rendent la main au moteur (bank) avant tout combat sur la carte.
        sb.AppendLine("    if GATHER then gatherAll() end");
        sb.AppendLine("    if GATHER and limitReached() then return false end");
        if (p.BankMap > 0 || withHdv) sb.AppendLine("    if GATHER and getPodsPercent() >= MAX_PODS then return true end");
        sb.AppendLine("    if not FIGHT then return true end");
        sb.AppendLine("    if #MAPS == 1 then");
        sb.AppendLine("        local ok, reason = attempt(fight, true)");
        // Un combat qui echoue (action_failed, groupe deja pris, refus serveur) ne tue PLUS l'expedition : on patiente
        // et on repart au tour suivant. Shadowbot s'arretait apres 3 essais sur une carte (log 09:33, « attempts=3 reason=action_failed »).
        sb.AppendLine("        if not ok then printWarning('Expedition : combat indisponible (reason=' .. tostring(reason) .. '), on continue') delay(2000) elseif lastFightLost() then return onDefeat() else DEFEATS = 0 end");
        sb.AppendLine("    else");
        sb.AppendLine("        while not limitReached() do");
        if (p.BankMap > 0 || withHdv) sb.AppendLine("            if getPodsPercent() >= MAX_PODS then break end   -- rendre la main au moteur pour bank()");
        sb.AppendLine("            local ok, reason = attempt(fight, true)");
        sb.AppendLine("            if not ok then if reason ~= 'no_matching_group' then printWarning('Expedition : combat indisponible (reason=' .. tostring(reason) .. '), carte suivante') end break end");
        sb.AppendLine("            if lastFightLost() then return onDefeat() end");
        sb.AppendLine("            DEFEATS = 0");
        sb.AppendLine("        end");
        sb.AppendLine("    end");
        sb.AppendLine("    return true");
        sb.AppendLine("end");
        return sb.ToString();
    }

    /// <summary>Script d'un DONJON : vérifie les clés, rejoint l'entrée puis la 1re salle, vide chaque salle (le jeu téléporte à la
    /// suivante après chaque victoire), sort après le boss et recommence tant que le nombre de passages et les clés le permettent.</summary>
    public static string BuildDungeon(DungeonProfile p, List<string>? warnings = null)
    {
        static string I(long v) => v.ToString(CultureInfo.InvariantCulture);
        static string Q(string t) => "'" + (t ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", " ").Replace("\n", " ") + "'";
        var rooms = p.Rooms.Where(r => r > 0).Distinct().ToList();
        if (rooms.Count == 0) warnings?.Add("donjon sans salle connue");
        if (p.EntranceMap <= 0) warnings?.Add("entrée du donjon inconnue : le perso doit déjà être à l'intérieur");
        string name = (p.Name ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (name.Length == 0) name = "Donjon";

        var sb = new StringBuilder();
        sb.AppendLine($"-- Donjon « {name} » : script généré, modifiable à la main.");
        sb.AppendLine("config:setMinMonsters(1)");
        sb.AppendLine("config:setMaxMonsters(8)");
        sb.AppendLine("config:setMandatoryMonsters({})");
        sb.AppendLine("config:setForbiddenMonsters({})");
        sb.AppendLine("config:setAmountOfSpecificMonsters({})");
        sb.AppendLine();
        sb.AppendLine($"local ROOMS = {{ {string.Join(", ", rooms.Select(I))} }}");
        sb.AppendLine($"local ENTRANCE = {I(Math.Max(0, p.EntranceMap))}");
        sb.AppendLine("local KEYS = { " + string.Join(", ", p.Keys.Where(k => k.Gid > 0).Select(k => $"{{ {I(k.Gid)}, {Math.Max(1, k.Qty)}, {Q(k.Name)} }}")) + " }");
        sb.AppendLine($"local RUNS = {Math.Max(0, p.Runs)}");
        sb.AppendLine($"local MAX_MINUTES = {Math.Max(0, p.MaxMinutes)}");
        sb.AppendLine($"local STOP_PODS = {Math.Clamp(p.StopAtPodsPercent, 0, 100)}");
        sb.AppendLine($"local BANK_MAP, BANK_DOOR, BANK_INSIDE = {I(Math.Max(0, p.BankMap))}, {p.BankDoor}, {I(Math.Max(0, p.BankInside))}");
        sb.AppendLine("local done, idle, lastRoom = 0, 0, 0");
        sb.AppendLine("local bossWon = false");
        sb.AppendLine("local okTime, startedAt = pcall(os.time)");
        AppendDefeatPolicy(sb, "Donjon", p.ContinueAfterDeath);
        sb.AppendLine();
        // Pas de bank() globale ici : le moteur l'appellerait en pleine salle (sortie du donjon en plein passage). Le dépôt se fait
        // ENTRE deux passages, une fois dehors (depositAtBank), et seulement si une banque est connue.
        sb.AppendLine("local function depositAtBank()");
        sb.AppendLine("    if BANK_MAP <= 0 then return false end");
        sb.AppendLine("    printMessage('Donjon : pods pleins, passage à la banque')");
        sb.AppendLine("    local target = (BANK_DOOR >= 0 and BANK_INSIDE > 0) and BANK_INSIDE or BANK_MAP");
        sb.AppendLine("    if not travelTo(target) and not travelTo(BANK_MAP) then printWarning('Banque : trajet impossible') return false end");
        sb.AppendLine("    if BANK_DOOR >= 0 and map:currentMapId() == BANK_MAP then map:door(BANK_DOOR) delay(1500) end");
        sb.AppendLine("    if not npc:npcBank() then printWarning('Banque : banquier introuvable') return false end");
        sb.AppendLine("    local deposited = exchange:putAllItems()");
        sb.AppendLine("    bank:close()");
        sb.AppendLine("    if not deposited then printWarning('Banque : dépôt non confirmé, arrêt') return false end");
        sb.AppendLine("    return getPodsPercent() < STOP_PODS");
        sb.AppendLine("end");
        sb.AppendLine();
        if (p.Elixir is { ItemGid: > 0, AlterationId: > 0 } elixir) AppendElixir(sb, elixir);
        sb.AppendLine("local function roomIndex()");
        sb.AppendLine("    local here = map:currentMapId()");
        sb.AppendLine("    for i, m in ipairs(ROOMS) do if m == here then return i end end");
        sb.AppendLine("    return 0");
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("local function hasKeys()");
        sb.AppendLine("    for _, k in ipairs(KEYS) do");
        sb.AppendLine("        if getInventoryItemCount(k[1]) < k[2] then printWarning('Donjon : clé « ' .. k[3] .. ' » x' .. k[2] .. ' absente du sac — tentative quand même (un trousseau peut la fournir)') end");
        sb.AppendLine("    end");
        sb.AppendLine("    return true");
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("local function limitReached()");
        sb.AppendLine("    if RUNS > 0 and done >= RUNS then printSuccess('Donjon : ' .. done .. ' passage(s) terminé(s)') return true end");
        sb.AppendLine("    if MAX_MINUTES > 0 and okTime and os.time() - startedAt >= MAX_MINUTES * 60 then printSuccess('Donjon : temps écoulé') return true end");
        sb.AppendLine("    if STOP_PODS > 0 and getPodsPercent() >= STOP_PODS and not depositAtBank() then printWarning('Donjon : pods à ' .. getPodsPercent() .. '%, arrêt') return true end");
        sb.AppendLine("    return false");
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("function move()");
        sb.AppendLine("    if #ROOMS == 0 then printError('Donjon : aucune salle connue') return false end");
        sb.AppendLine("    if MAX_MINUTES > 0 and okTime and os.time() - startedAt >= MAX_MINUTES * 60 then printSuccess('Donjon : temps écoulé') return false end");
        sb.AppendLine("    local room = roomIndex()");
        sb.AppendLine("    if room == 0 then");
        sb.AppendLine("        -- Dehors : entre deux passages (ou au lancement).");
        sb.AppendLine("        if lastRoom == #ROOMS and bossWon then done = done + 1 printSuccess('Donjon : passage ' .. done .. ' terminé') end");
        sb.AppendLine("        lastRoom = 0 bossWon = false");
        sb.AppendLine("        if limitReached() then return false end");
        sb.AppendLine("        if not hasKeys() then return false end");
        if (p.Elixir is { ItemGid: > 0, AlterationId: > 0 }) sb.AppendLine("        ensureElixir()   -- dehors, avant le passage : jamais en pleine salle");
        sb.AppendLine("        if ENTRANCE > 0 and not travelTo(ENTRANCE) then printWarning('Donjon : entrée injoignable') delay(5000) return true end");
        sb.AppendLine("        if not travelTo(ROOMS[1]) then");
        sb.AppendLine("            printError('Donjon : entrée à faire à la main (PNJ / porte du donjon), le script reprend dès la 1re salle')");
        sb.AppendLine("            delay(10000)");
        sb.AppendLine("        end");
        sb.AppendLine("        return true");
        sb.AppendLine("    end");
        sb.AppendLine("    if room ~= lastRoom then printMessage('Donjon : salle ' .. room .. '/' .. #ROOMS) lastRoom = room idle = 0 end");
        sb.AppendLine("    local before = combat and combat.lastFightResult and combat:lastFightResult()");
        sb.AppendLine("    if fight() then");
        sb.AppendLine("        local result = combat and combat.lastFightResult and combat:lastFightResult()");
        sb.AppendLine("        if result and result.lost == true then lastRoom = 0 bossWon = false return onDefeat() end");
        sb.AppendLine("        if result and result.won == true then DEFEATS = 0 end");
        sb.AppendLine("        if room == #ROOMS and result and result.won == true and (not before or result.fightId ~= before.fightId) then bossWon = true end");
        sb.AppendLine("        idle = 0 return true");
        sb.AppendLine("    end");
        sb.AppendLine("    idle = idle + 1");
        sb.AppendLine("    if room == #ROOMS then");
        sb.AppendLine("        -- Salle du boss vidée : bouton de sortie (map:leaveDungeon). Le passage est compté une fois dehors.");
        sb.AppendLine("        local left, why = map:leaveDungeon()");
        sb.AppendLine("        if not left then printError('Donjon : sortie refusée (' .. tostring(why) .. ')') delay(2000) end");
        sb.AppendLine("        return true");
        sb.AppendLine("    end");
        sb.AppendLine("    -- Salle vide sans téléportation (déjà nettoyée, groupe en cours de repousse) : salle suivante à pied après ~6 s.");
        sb.AppendLine("    if idle >= 3 then travelTo(ROOMS[room + 1]) idle = 0 else delay(2000) end");
        sb.AppendLine("    return true");
        sb.AppendLine("end");
        return sb.ToString();
    }
}

/// <summary>
/// Offline, transport-agnostic snapshot supplied by the Expedition UI.
/// The gate tests preconditions for a Lua expedition, never sends a packet,
/// never pretends to observe a server ACK and leaves MITM untouched.
/// Located in the pure ExpeditionScript module already linked by offline tests.
/// </summary>
public sealed record ExpeditionSocketSessionSnapshot(
    bool IsSocketSession,
    bool AccountRunning,
    bool SessionParked,
    bool SessionBackoff,
    bool Reconnecting,
    bool WaitingForNewSession,
    bool GameLinkDown,
    bool HasServerMap,
    bool HasFightPermission,
    bool BottingAuthorized = true,
    bool LevelAllowed = true,
    bool IsFollowerLocked = false,
    bool InjectorRegistered = true);

public static class ExpeditionSocketSessionGate
{
    /// <summary>
    /// null = no known preflight blocker, NOT evidence that a live injector,
    /// working movement/combat forge or a server acknowledgement exists.
    /// </summary>
    public static string? StartBlockReason(ExpeditionSocketSessionSnapshot session)
    {
        if (!session.IsSocketSession) return null; // Preserve MITM behavior.
        if (!session.AccountRunning)
            return "Socket : compte hors ligne. Connecte-le avant de lancer l'expédition.";
        if (session.SessionParked || session.SessionBackoff || session.Reconnecting || session.WaitingForNewSession)
            return "Socket : session en attente / reconnexion. Attends une nouvelle session de jeu authentifiée.";
        if (session.GameLinkDown)
            return "Socket : lien du jeu coupé. Aucune expédition ne sera lancée à l'aveugle.";
        if (!session.HasServerMap)
            return "Socket : aucune carte confirmée par le serveur. Attends le chargement de la carte.";
        if (!session.InjectorRegistered)
            return "Socket : injecteur du canal de commande non enregistré pour cette session. Aucun ordre de jeu ne peut partir.";
        if (!session.BottingAuthorized)
            return "Socket : botting non autorisé par la licence ou suspendu. Aucun ordre de jeu ne sera envoyé.";
        if (!session.LevelAllowed)
            return "Socket : niveau supérieur au plafond autorisé. L'injection des ordres de jeu est bloquée.";
        if (session.IsFollowerLocked)
            return "Socket : ce personnage est verrouillé en suiveur d'équipe.";
        if (!session.HasFightPermission)
            return "Socket : droit Combat absent. Les expéditions nécessitent ce droit pour attaquer les groupes.";
        return null;
    }
}
