using System.Text;

namespace GivreClone.Engine.LuaTooling;

/// <summary>Modèle de script complet proposé par l'éditeur (« Nouveau depuis un modèle »).</summary>
public sealed record LuaScriptTemplate(string Id, string Title, string Description, string Category, string Body);

/// <summary>Extrait insérable. Body : ${1:texte} = arrêt de tabulation (même numéro = édition liée), $0 = curseur final,
/// \n = saut de ligne, indentation 4 espaces (relative à la ligne d'insertion). Voir <see cref="LuaSnippetSyntax"/>.</summary>
public sealed record LuaSnippet(string Trigger, string Title, string Description, string Body);

/// <summary>Arrêt de tabulation d'un extrait déployé : Index (1..n, liés s'ils partagent l'index), position dans le texte déployé.</summary>
public readonly record struct LuaSnippetStop(int Index, int Offset, int Length);

/// <summary>Extrait déployé : texte final (espaces réservés remplacés par leur texte par défaut), arrêts triés par
/// (Index, Offset), position du curseur final ($0, sinon fin du texte).</summary>
public sealed record LuaSnippetExpansion(string Text, IReadOnlyList<LuaSnippetStop> Stops, int FinalCaretOffset);

/// <summary>Syntaxe des extraits : ${N:défaut} (N ≥ 1, défaut sans '}', '$' ni saut de ligne), $N (arrêt vide), $0
/// (curseur final, une fois au plus), $$ = '$' littéral. Pur, sans UI.</summary>
public static class LuaSnippetSyntax
{
    /// <summary>Déploie body. lineIndent est ajouté après chaque saut de ligne (indentation de la ligne d'insertion) ;
    /// indentUnit (ex. "\t" ou "  ") remplace chaque groupe de 4 espaces EN TÊTE de ligne. Best effort : un '$'
    /// mal formé est gardé tel quel (voir <see cref="Validate"/>).</summary>
    public static LuaSnippetExpansion Expand(string body, string lineIndent = "", string? indentUnit = null)
    {
        body ??= "";
        var sb = new StringBuilder(body.Length + 32);
        var stops = new List<LuaSnippetStop>();
        int final = -1;
        bool lineStart = true;
        for (int i = 0; i < body.Length;)
        {
            char c = body[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n')
            {
                sb.Append('\n').Append(lineIndent);
                lineStart = true;
                i++;
                continue;
            }
            if (lineStart && indentUnit is not null && indentUnit != "    " && c == ' ')
            {
                int n = 0;
                while (i + n < body.Length && body[i + n] == ' ') n++;
                for (int k = 0; k < n / 4; k++) sb.Append(indentUnit);
                sb.Append(' ', n % 4);
                i += n;
                lineStart = false;
                continue;
            }
            lineStart = false;
            if (c == '$' && TryReadPlaceholder(body, i, out int index, out string text, out int consumed))
            {
                if (index == 0) { if (final < 0) final = sb.Length; }
                else stops.Add(new LuaSnippetStop(index, sb.Length, text.Length));
                sb.Append(text);
                i += consumed;
                continue;
            }
            if (c == '$' && i + 1 < body.Length && body[i + 1] == '$') { sb.Append('$'); i += 2; continue; }
            sb.Append(c);
            i++;
        }
        stops.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Offset.CompareTo(b.Offset));
        return new LuaSnippetExpansion(sb.ToString(), stops, final < 0 ? sb.Length : final);
    }

    /// <summary>Texte par défaut du corps (espaces réservés remplacés par leur défaut, sans indentation ajoutée).</summary>
    public static string DefaultText(string body) => Expand(body).Text;

    /// <summary>Problèmes de syntaxe (vide = bien formé) : '$' isolé, accolade non fermée, N non contigus (1..max),
    /// arrêts liés à défauts différents, plusieurs $0, ${0:…}.</summary>
    public static IReadOnlyList<string> Validate(string body)
    {
        var errors = new List<string>();
        var defaults = new Dictionary<int, string>();
        int finals = 0;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '$') continue;
            if (i + 1 < body.Length && body[i + 1] == '$') { i++; continue; }
            if (!TryReadPlaceholder(body, i, out int index, out string text, out int consumed))
            {
                errors.Add($"'$' mal formé à la position {i}");
                continue;
            }
            if (index == 0)
            {
                finals++;
                if (body[i + 1] == '{') errors.Add("${0:…} interdit : utilise $0");
            }
            else if (defaults.TryGetValue(index, out var prev))
            {
                if (prev != text) errors.Add($"arrêt ${index} lié avec des défauts différents (« {prev} » / « {text} »)");
            }
            else defaults[index] = text;
            i += consumed - 1;
        }
        if (finals > 1) errors.Add("plusieurs $0");
        int max = defaults.Count == 0 ? 0 : defaults.Keys.Max();
        for (int n = 1; n <= max; n++)
            if (!defaults.ContainsKey(n)) errors.Add($"arrêt ${n} manquant (numérotation non contiguë)");
        return errors;
    }

    // "$N" ou "${N:texte}" à la position i. consumed = longueur lue.
    private static bool TryReadPlaceholder(string s, int i, out int index, out string text, out int consumed)
    {
        index = 0; text = ""; consumed = 0;
        if (i + 1 >= s.Length) return false;
        int j = i + 1;
        if (char.IsAsciiDigit(s[j]))
        {
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            if (!int.TryParse(s.AsSpan(i + 1, j - i - 1), out index)) return false;
            consumed = j - i;
            return true;
        }
        if (s[j] != '{') return false;
        j++;
        int numStart = j;
        while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
        if (j == numStart || j >= s.Length || s[j] != ':') return false;
        if (!int.TryParse(s.AsSpan(numStart, j - numStart), out index)) return false;
        j++;
        int textStart = j;
        while (j < s.Length && s[j] != '}')
        {
            if (s[j] is '$' or '\n' or '{') return false;
            j++;
        }
        if (j >= s.Length) return false;
        text = s[textStart..j];
        consumed = j + 1 - i;
        return true;
    }
}

/// <summary>Modèles de scripts et extraits de l'éditeur Lua. Tous les noms d'API utilisés existent dans
/// docs/portal/lua-api-doc.json ou dans <see cref="LuaStdLib"/> ; chaque corps compile sous MoonSharp (vérifié par test
/// et par sonde).</summary>
public static class LuaTemplates
{
    public static IReadOnlyList<LuaSnippet> Snippets { get; } = BuildSnippets();

    public static IReadOnlyList<LuaScriptTemplate> Scripts { get; } = BuildScripts();

    // Fins de ligne normalisées (\n) quel que soit l'encodage du fichier source.
    private static string N(string s) => s.ReplaceLineEndings("\n");

    private static List<LuaSnippet> BuildSnippets() =>
    [
        // ---- Structures du langage ----
        new("function", "Fonction globale", "Déclare une fonction globale (visible partout dans le script).",
            N("""
            function ${1:nom}(${2:arg})
                $0
            end
            """)),
        new("localfunction", "Fonction locale", "Déclare une fonction locale : à placer AVANT le code qui l'appelle.",
            N("""
            local function ${1:nom}(${2:arg})
                $0
            end
            """)),
        new("if", "Bloc if", "Condition simple : exécute le bloc si la condition est vraie.",
            N("""
            if ${1:condition} then
                $0
            end
            """)),
        new("ifelse", "Bloc if / else", "Condition avec une branche « sinon ».",
            N("""
            if ${1:condition} then
                ${2:-- si vrai}
            else
                ${3:-- sinon}
            end$0
            """)),
        new("ifelseif", "Bloc if / elseif / else", "Plusieurs cas successifs : elseif s'écrit en un seul mot.",
            N("""
            if ${1:condition} then
                ${2:-- cas 1}
            elseif ${3:autreCondition} then
                ${4:-- cas 2}
            else
                ${5:-- sinon}
            end$0
            """)),
        new("fori", "Boucle for numérique", "Compte de début à fin (bornes incluses), avec un pas optionnel.",
            N("""
            for ${1:i} = ${2:1}, ${3:10} do
                $0
            end
            """)),
        new("forpairs", "Boucle for … pairs", "Parcourt toutes les clés d'une table (ordre non garanti).",
            N("""
            for ${1:cle}, ${2:valeur} in pairs(${3:t}) do
                $0
            end
            """)),
        new("foripairs", "Boucle for … ipairs", "Parcourt une liste dans l'ordre 1, 2, 3… (ex. map:getMonsters()).",
            N("""
            for ${1:_}, ${2:v} in ipairs(${3:liste}) do
                $0
            end
            """)),
        new("while", "Boucle while", "Répète tant que la condition est vraie.",
            N("""
            while ${1:condition} do
                $0
            end
            """)),
        new("repeat", "Boucle repeat / until", "Répète au moins une fois, jusqu'à ce que la condition devienne vraie.",
            N("""
            repeat
                $0
            until ${1:condition}
            """)),
        new("continue", "Passer à l'élément suivant (goto continue)",
            "Lua n'a pas de « continue » : saute à ::continue:: en fin de boucle. Garde le do … end : MoonSharp refuse un goto qui saute par-dessus un local.",
            N("""
            for ${1:_}, ${2:v} in ipairs(${3:liste}) do
                if ${4:condition} then goto continue end
                do
                    $0
                end
                ::continue::
            end
            """)),
        new("pcall", "Bloc protégé (pcall)", "Exécute un bloc sans arrêter le script en cas d'erreur, puis affiche l'erreur.",
            N("""
            local ${1:ok}, ${2:err} = pcall(function()
                $0
            end)
            if not ${1:ok} then
                printError("Erreur : " .. tostring(${2:err}))
            end
            """)),

        // ---- Points d'entrée GivreClone ----
        new("moveroute", "move() déclaratif (trajet)",
            "La fonction move() renvoie la liste des étapes : une carte par ligne, le moteur récolte / combat puis suit path.",
            N("""
            function move()
                return {
                    { map = "${1:4,-22}", path = "${2:right}", gather = ${3:true}, fight = ${4:false} },
                    $0
                }
            end
            """)),
        new("moveloop", "move() impératif", "La fonction move() pilote directement : récolte, combat, puis change de carte (rappelée en boucle).",
            N("""
            function move()
                gatherAll()
                fight()
                if not changeMap("${1:right}") then
                    delay(2000)
                end$0
            end
            """)),
        new("bank", "bank() (dépôt en banque)",
            "Appelée quand les pods atteignent MAX_PODS : le bot voyage jusqu'au banquier et dépose tout le sac.",
            N("""
            function bank()
                return {
                    { map = ${1:2320}, npcBank = true },   -- carte du banquier (id exact : le bot y voyage)
                }
            end$0
            """)),
        new("onTurn", "onTurn() (IA de tour)",
            "Joue ton tour de combat : cible le premier ennemi vivant, s'approche si besoin et lance le sort.",
            N("""
            function onTurn()
                local cible
                for _, e in ipairs(combat:enemies()) do
                    if e.alive then cible = e break end
                end
                if not cible then return end
                if not combat:canCast(${1:161}, cible.cell) then
                    combat:moveToCell(cible.cell)
                end
                combat:castOnCell(${1:161}, cible.cell)$0
            end   -- fin de tour automatique au retour
            """)),
        new("onPlacement", "onPlacement() (placement)", "Choisit la case de placement au début du combat ; « prêt » automatique au retour.",
            N("""
            function onPlacement()
                local cases = combat:placementCells()
                if #cases > 0 then
                    combat:place(cases[${1:1}].cell)
                end$0
            end
            """)),
        new("onFightEnd", "onFightEnd(r) (bilan de combat)", "Appelée une fois après chaque combat avec son bilan (won, lost, loot…).",
            N("""
            function onFightEnd(${1:r})
                if ${1:r}.won then
                    printSuccess("Victoire, " .. #${1:r}.loot .. " lignes de butin")
                elseif ${1:r}.lost then
                    printWarning("${2:Défaite}")
                end$0
            end
            """)),
        new("onPrivateMessage", "onPrivateMessage (réponse aux MP)",
            "Appelée à chaque MP reçu ; répond si le texte contient un mot-clé (permission « Réponse automatique »).",
            N("""
            function onPrivateMessage(from, text, senderId)
                if string.find(string.lower(text), "${1:salut}", 1, true) then
                    sendPrivateMessage(from, "${2:Salut ! Je suis occupé, je te réponds plus tard.}")
                end$0
            end
            """)),
        new("stopped", "stopped(reason) (fin du run)",
            "Appelée une fois à l'arrêt du script : seulement print*, memory:*, sendWebhook et des lectures (budget 2 s).",
            N("""
            function stopped(reason)
                printWarning("Script arrêté : " .. reason)
                memory:set("${1:dernierArret}", reason)$0
            end
            """)),
        new("messagesRegistering", "messagesRegistering (hooks de paquets)",
            "Enregistre des gestionnaires de paquets une fois le script chargé (registerMessage).",
            N("""
            function messagesRegistering()
                registerMessage("${1:ChatChannelMessageEvent}", function(p)
                    $0
                end)
            end
            """)),
        new("config", "Bloc de configuration", "Réglages lus par le moteur : pods, ressources, taille des groupes, régénération.",
            N("""
            MAX_PODS = ${1:90}                        -- % de pods qui déclenche bank()
            ELEMENTS_TO_GATHER = { ${2:38} }          -- ElementTypeId (38 = Blé) ; vide = toutes les ressources
            MIN_MONSTERS = ${3:1}                     -- taille min d'un groupe attaqué
            MAX_MONSTERS = ${4:4}                     -- taille max d'un groupe attaqué
            FORBIDDEN_MONSTERS = { ${5:"Bouftou Royal"} }   -- groupes jamais attaqués (nom ou genericId)
            MIN_LIFE_PERCENT = ${6:50}                -- régénère avant un combat sous ce % de PV
            OPEN_BAGS = ${7:true}                     -- pods pleins : ouvre les sacs de ressources
            $0
            """)),
        new("travel", "Voyage vers une carte", "Voyage multi-cartes avec travelTo (zaap / havre-sac si utile), en gérant l'échec.",
            N("""
            if not travelTo(${1:212861955}) then
                printWarning("Voyage impossible vers ${1:212861955}")
            end$0
            """)),
        new("printSuccess", "printSuccess (message vert)", "Affiche un message de succès (vert) dans la console du bot.",
            N("""printSuccess("${1:message}")$0""")),
        new("printWarning", "printWarning (message jaune)", "Affiche un avertissement (jaune) dans la console du bot.",
            N("""printWarning("${1:message}")$0""")),
    ];

    private static List<LuaScriptTemplate> BuildScripts() =>
    [
        new("recolte-courte", "Récolte courte sur place",
            "Effectue au maximum trois passages de récolte sur la carte actuelle, puis termine sans déplacement ni achat.",
            "Récolte", Script("""
            -- Récolte courte : adapter le type de ressource à son métier.
            ELEMENTS_TO_GATHER = { 38 }
            MAX_PODS = 90
            local passages = 0
            function move()
                if passages >= 3 or getPodsPercent() >= MAX_PODS then return false end
                passages = passages + 1
                gatherAll()
                return passages < 3
            end
            """)),
        new("recolte-banque", "Récolte + banque (trajet déclaratif)",
            "Boucle de récolte sur 4 cartes d'Astrub, dépôt automatique en banque quand les pods sont pleins.",
            "Récolte", Script("""
            -- Récolte du Blé dans les champs d'Astrub, avec dépôt automatique en banque.
            -- Adapte ELEMENTS_TO_GATHER (ElementTypeId) et les cartes du trajet à ton métier.

            ELEMENTS_TO_GATHER = { 38 }   -- 38 = Blé ; vide = toutes les vraies ressources
            MAX_PODS = 90                 -- dès 90 % de pods, le moteur joue bank() au lieu de move()
            OPEN_BAGS = true              -- pods pleins : ouvre d'abord les sacs de ressources

            -- Trajet déclaratif : une étape par carte (coordonnées "x,y" ou id).
            -- Sur chaque carte, le moteur récolte tant qu'il reste une ressource, puis suit path.
            -- Après la dernière carte, la boucle repart de la première.
            function move()
                return {
                    { map = "4,-22", gather = true, path = "right" },
                    { map = "5,-22", gather = true, path = "bottom" },
                    { map = "5,-21", gather = true, path = "left" },
                    { map = "4,-21", gather = true, path = "top" },
                }
            end

            -- Pods pleins : le bot voyage jusqu'au banquier (id exact, zaap / havre-sac si utile)
            -- et dépose tout le sac, puis revient sur le trajet.
            function bank()
                return {
                    { map = 2320, npcBank = true },   -- banque d'Astrub (intérieur)
                }
            end

            -- Petit bilan à chaque arrêt du script.
            function stopped(reason)
                printMessage("Arrêt (" .. reason .. ") : " .. stats:harvests() .. " récoltes, "
                    .. stats:resources() .. " ressources", "info")
            end
            """)),

        new("combat-trajet", "Combat en boucle (MIN/MAX_MONSTERS)",
            "Enchaîne les combats sur un trajet, avec filtres de groupes, régénération avant combat et banque.",
            "Combat", Script("""
            -- Combat en boucle sur quelques cartes : le bot attaque les groupes éligibles puis avance.
            -- Les tours sont joués par le moteur avec le profil de l'onglet Combat
            -- (ajoute un onTurn() pour écrire ta propre IA : voir le modèle « IA de combat »).
            -- Remplace les cartes du trajet par ta zone de combat.

            MIN_MONSTERS = 2                           -- groupes d'au moins 2 monstres…
            MAX_MONSTERS = 4                           -- … et d'au plus 4
            FORBIDDEN_MONSTERS = { "Bouftou Royal" }   -- jamais un groupe qui contient ce monstre (nom ou genericId)
            MIN_LIFE_PERCENT = 50                      -- sous 50 % de PV : régénère avant d'engager…
            REGEN_UP_TO_PERCENT = 90                   -- … jusqu'à 90 %
            MAX_PODS = 90
            -- AUTO_DELETE = { 311 }                   -- GID détruits quand les pods sont pleins (avant bank())

            function move()
                return {
                    { map = "4,-22", fight = true, path = "right" },
                    { map = "5,-22", fight = true, path = "bottom" },
                    { map = "5,-21", fight = true, path = "left" },
                    { map = "4,-21", fight = true, path = "top" },
                }
            end

            function bank()
                return {
                    { map = 2320, npcBank = true },   -- banque d'Astrub (intérieur)
                }
            end

            -- Bilan de chaque combat (appelé une fois, hors combat, entre deux move()).
            function onFightEnd(r)
                if r.won then
                    printSuccess("Victoire en " .. math.floor((r.durationMs or 0) / 1000) .. " s, "
                        .. #r.loot .. " lignes de butin")
                elseif r.lost then
                    printWarning("Défaite : le moteur s'occupe de la résurrection au Phénix")
                else
                    printMessage("Issue du combat inconnue", "debug")
                end
                printMessage(stats:won() .. " victoires sur " .. stats:fights() .. " combats", "info")
            end
            """)),

        new("recolte-combat", "Récolte et combat mêlés",
            "Un même trajet qui récolte, attaque les petits groupes et régénère sur une carte « camp ».",
            "Mixte", Script("""
            -- Récolte + combat sur le même trajet : sur chaque carte, le moteur récolte d'abord,
            -- attaque ensuite un groupe éligible, puis suit path.

            ELEMENTS_TO_GATHER = { 38, 43 }   -- Blé, Orge
            MAX_PODS = 85
            MIN_MONSTERS = 1
            MAX_MONSTERS = 3                  -- petits groupes seulement
            MIN_LIFE_PERCENT = 40             -- régénère avant un combat sous 40 % de PV
            OPEN_BAGS = true

            function move()
                return {
                    { map = "4,-22", gather = true, fight = true, path = "right" },
                    { map = "5,-22", gather = true, fight = true, path = "bottom" },
                    -- carte « camp » : régénère sous 60 % de PV avant de récolter
                    { map = "5,-21", regeneration = 60, gather = true, path = "left" },
                    -- bloc libre joué sur la carte avant de la quitter
                    { map = "4,-21", gather = true, fight = true, custom = function()
                          if getPodsPercent() >= 70 then
                              printMessage("Pods à " .. getPodsPercent() .. " %", "debug")
                          end
                      end, path = "top" },
                }
            end

            function bank()
                return {
                    { map = 2320, npcBank = true },   -- banque d'Astrub (intérieur)
                }
            end

            function onFightEnd(r)
                if r.lost then printWarning("Combat perdu") end
            end

            function stopped(reason)
                printMessage("Fin (" .. reason .. ") : " .. stats:harvests() .. " récoltes, "
                    .. stats:won() .. " victoires sur " .. stats:fights() .. " combats", "info")
            end
            """)),

        new("combat-ia", "IA de combat (onTurn / onPlacement)",
            "Écris ton propre tour : placement loin des ennemis, cible la plus faible, lancers, puis recul.",
            "Combat", Script("""
            -- IA de combat en Lua : onPlacement() choisit la case, onTurn() joue chacun de tes tours.
            -- Tant que onTurn existe, il REMPLACE l'auto-combat du profil (onglet Combat).
            -- Remplace SORT_ATTAQUE par l'id de TON sort à distance (liste : combat:getSpells()).

            TURN_TIME_LIMIT = 15000   -- budget d'un tour en ms : dépassé, le tour est fini d'office
            MIN_MONSTERS = 1
            MAX_MONSTERS = 4
            MIN_LIFE_PERCENT = 50

            local SORT_ATTAQUE = 161   -- id du sort d'attaque
            local LANCERS_MAX = 3      -- lancers par tour au plus
            local SEUIL_PV = 25        -- sous 25 % de PV, le moteur joue le tour avec le profil

            -- Ennemi vivant le plus faible (en % de PV).
            local function cibleLaPlusFaible()
                local best
                for _, e in ipairs(combat:enemies()) do
                    if e.alive and (not best or e.hpPercent < best.hpPercent) then best = e end
                end
                return best
            end

            -- Distance entre une case et l'ennemi vivant le plus proche.
            local function distanceEnnemi(cell)
                local dmin = math.huge
                for _, e in ipairs(combat:enemies()) do
                    if e.alive then dmin = math.min(dmin, map:cellDistance(cell, e.cell)) end
                end
                return dmin
            end

            function onPlacement()
                -- sort à distance : on se place le plus loin possible des ennemis
                local best, bestD = nil, -1
                for _, c in ipairs(combat:placementCells()) do
                    local d = distanceEnnemi(c.cell)
                    if d > bestD then best, bestD = c.cell, d end
                end
                if best then combat:place(best) end
            end   -- « prêt » automatique au retour

            function onTurn()
                if combat:getHp() < combat:getMaxHp() * SEUIL_PV / 100 then
                    combat:playTurn()   -- PV bas : le moteur joue tout ce tour
                    return
                end

                local cible = cibleLaPlusFaible()
                if not cible then return end

                -- Sort pas lançable d'ici : cherche une case atteignable d'où il l'est
                if not combat:canCast(SORT_ATTAQUE, cible.cell) then
                    for _, c in ipairs(combat:reachableCells()) do
                        if combat:canCast(SORT_ATTAQUE, cible.cell, c.cell) then
                            combat:moveToCell(c.cell)
                            break
                        end
                    end
                end

                -- Lance tant que c'est possible (PA, relance, lancers par tour)
                for _ = 1, LANCERS_MAX do
                    local ok, raison = combat:canCast(SORT_ATTAQUE, cible.cell)
                    if not ok then
                        printMessage("Plus de lancer possible (raison " .. tostring(raison) .. ")", "debug")
                        break
                    end
                    combat:castOnCell(SORT_ATTAQUE, cible.cell)
                end

                -- PM restants : s'éloigner des ennemis (attention : quitter le contact peut être tacle)
                local _, pm = combat:pointsLeft()
                local ici = combat:getCell()
                if pm > 0 and ici >= 0 then
                    local loin, dmax = nil, distanceEnnemi(ici)
                    for _, c in ipairs(combat:reachableCells()) do
                        local d = distanceEnnemi(c.cell)
                        if d > dmax then loin, dmax = c.cell, d end
                    end
                    if loin then combat:moveToCell(loin) end
                end
            end   -- fin de tour automatique au retour

            function move()
                return {
                    { map = "4,-22", fight = true, path = "right" },
                    { map = "5,-22", fight = true, path = "bottom" },
                    { map = "5,-21", fight = true, path = "left" },
                    { map = "4,-21", fight = true, path = "top" },
                }
            end

            function onFightEnd(r)
                if r.won then printSuccess("Victoire !") elseif r.lost then printWarning("Défaite") end
            end
            """)),

        new("classement-prudent", "Classement des cartes observées (lecture seule)",
            "Compare les récoltes par passage après cinq visites minimum ; exclut les échantillons insuffisants.",
            "Récolte", Script("""
            -- Comparaison descriptive des cartes de la heatmap locale ; ni rentabilité ni confiance statistique garanties.
            local MIN_VISITES = 5
            function move()
                local bilan = stats:summary("week")
                local cartes = {}
                local exclus = 0
                for _, carte in ipairs(bilan.maps or {}) do
                    if carte.visits >= MIN_VISITES then
                        cartes[#cartes + 1] = { map = carte.map, visites = carte.visits,
                            taux = carte.harvests / carte.visits }
                    else
                        exclus = exclus + 1
                    end
                end
                table.sort(cartes, function(a, b)
                    if a.taux == b.taux then return a.map < b.map end
                    return a.taux > b.taux
                end)
                if #cartes < 2 then
                    printWarning("Comparaison indisponible : moins de deux cartes avec cinq visites observées")
                    return false
                end
                for i = 1, math.min(10, #cartes) do
                    local carte = cartes[i]
                    printMessage(carte.map .. " : " .. carte.taux .. " récoltes/passage ; " .. carte.visites .. " visites", "info")
                end
                printWarning(exclus .. " carte(s) exclue(s), échantillon insuffisant. Classement descriptif, sans garantie de rendement futur.")
                return false
            end
            """)),

        new("rendement-horaire", "Rendement sur durée mesurée (lecture seule)",
            "Calcule les ressources par heure de botting enregistré et refuse une durée absente ou nulle.",
            "Récolte", Script("""
            -- Lecture du temps de session enregistré, pas des heures calendaires de la période.
            local PERIODE = "today"
            function move()
                local bilan = stats:summary(PERIODE)
                if not bilan.bottingSec or bilan.bottingSec <= 0 then
                    printWarning("Durée mesurée insuffisante : rendement indisponible")
                    return false
                end
                local parHeure = bilan.resources * 3600 / bilan.bottingSec
                printMessage("Durée mesurée : " .. bilan.bottingSec .. " s ; ressources/h : " .. parHeure, "info")
                return false
            end
            """)),

        new("rapport-kamas", "Rapport HDV après taxes (lecture seule)",
            "Affiche les ventes encaissées et taxes journalisées, sans confondre encaissement et bénéfice complet.",
            "HDV", Script("""
            -- Bilan du journal HDV local, sans ordre envoyé au jeu.
            local PERIODE = "today"
            function move()
                local bilan = stats:hdvSummary(PERIODE)
                if not bilan then
                    printWarning("Personnage non identifié : bilan indisponible")
                    return false
                end
                printMessage("Ventes : " .. bilan.sales .. " ; taxes : " .. bilan.taxes
                    .. " ; après taxes : " .. bilan.afterTaxes .. " kamas", "info")
                printWarning("Coût d'acquisition inconnu : ce montant n'est pas un bénéfice complet. Journal local potentiellement incomplet.")
                return false
            end
            """)),

        new("fm-preparation", "Préparation FM (lecture seule)",
            "Vérifie le support, le métier et son niveau sur les données locales, sans déplacement, achat ni rune.",
            "Métiers", Script("""
            -- Remplace l'UID/GID du support et l'identifiant du métier FM.
            local SUPPORT = 0
            local METIER = 64
            function move()
                local resultat, motif = fm:prepare(SUPPORT, METIER)
                if resultat then
                    printSuccess("Support niveau " .. resultat.itemLevel .. " ; métier niveau " .. resultat.jobLevel)
                else
                    printWarning(motif)
                end
                return false
            end
            """)),

        new("hdv-scan", "Scan HDV local borné (lecture seule)",
            "Lit les prix connus de 20 objets au maximum, indique leur source et leur âge, puis s'arrête. Aucun achat ni rafraîchissement réseau.",
            "HDV", Script("""
            -- Remplace les GID par les objets à observer. Un objet par appel de move().
            -- Prix du personnage/serveur courant ; un prix inconnu n'est jamais remplacé par zéro.
            local SCAN_GIDS = { 289, 303, 312 }
            local index = 1
            function move()
                if index > #SCAN_GIDS or index > 20 then return false end
                local gid = SCAN_GIDS[index]
                index = index + 1
                local devis = eco:price(gid)
                if devis then
                    local age = devis.ageMs and (tostring(devis.ageMs) .. " ms") or "inconnu"
                    printMessage(gid .. " : " .. devis.unit .. " kamas ; source " .. devis.source .. "; âge " .. age, "info")
                else
                    printWarning(gid .. " : prix inconnu")
                end
                return index <= #SCAN_GIDS and index <= 20
            end
            """)),

        new("hdv-vente", "Vente à l'HDV (sale:)",
            "Repositionne tes annonces puis met en vente le stock du sac, avec prix plancher et garde anti-appât.",
            "HDV", Script("""
            -- Vente à l'Hôtel de Vente : reprice de tes annonces, puis mise en vente du stock du sac.
            -- Permission « Hôtel de Vente » requise. Le script fait UN passage puis s'arrête
            -- (pratique dans une plage du planning).

            local HDV_MAP_ID = 191104004   -- HDV des ressources d'Astrub (carte de l'hôtelier)
            local SOUS_COTE = 1            -- kamas sous le lot le moins cher du marché
            local ANTI_APPAT = 0.5         -- lot à moins de 50 % de la moyenne = appât : on s'aligne sur la moyenne

            -- Objets à vendre : gid, taille du lot (1 / 10 / 100), prix plancher du LOT, lots gardés dans le sac
            local VENTES = {
                { gid = 289, lot = 100, plancher = 1500, garder = 0 },   -- Blé
                -- { gid = 2303, lot = 10, plancher = 800, garder = 1 },
            }

            -- Prix de vente d'un lot : sous-cote du marché, sauf lot appât ; jamais sous le plancher.
            local function prixDeVente(v)
                local minPrix = sale:GetPriceItem(v.gid, v.lot)          -- prix min du lot (-1 si inconnu)
                local moyenne = sale:getAveragePriceItem(v.gid, v.lot)   -- prix moyen du lot (-1 si inconnu)
                local prix = v.plancher
                if minPrix > 0 and (moyenne <= 0 or minPrix >= moyenne * ANTI_APPAT) then
                    prix = minPrix - SOUS_COTE
                elseif moyenne > 0 then
                    prix = math.floor(moyenne)
                end
                return math.max(prix, v.plancher)
            end

            -- Met en vente tous les lots disponibles d'un objet ; false si l'HDV est plein.
            local function vendre(v)
                local nom = getItemName(v.gid)
                local lots = math.floor(getInventoryItemCount(v.gid) / v.lot) - v.garder
                while lots > 0 do
                    if sale:AvailableSpace() == 0 then
                        printWarning("HDV plein : plus d'emplacement libre")
                        return false
                    end
                    local prix = prixDeVente(v)
                    if not sale:SellItem(v.gid, v.lot, prix) then
                        printError("Mise en vente refusée pour " .. nom)
                        return true
                    end
                    printSuccess(nom .. " x" .. v.lot .. " mis en vente à " .. prix .. " k")
                    lots = lots - 1
                end
                return true
            end

            function move()
                if not goToHdv(HDV_MAP_ID) then
                    printError("Impossible d'ouvrir l'HDV (carte " .. HDV_MAP_ID .. ")")
                    return false
                end
                -- 1) Annonces déjà en vente : prix min du marché - 1 (jamais à la hausse)
                local n = sale:UpdateAllItems()
                printMessage(n .. " annonce(s) repositionnée(s), " .. sale:ItemsOnSale() .. " en vente", "info")
                -- 2) Stock du sac
                for _, v in ipairs(VENTES) do
                    if not vendre(v) then break end
                end
                leaveDialog()
                printSuccess("Passage à l'HDV terminé : " .. getKamas() .. " kamas")
                return false   -- false = fin du script
            end
            """)),

        new("mp-reponses", "Réponses automatiques aux MP",
            "Écoute les messages privés et répond par mots-clés, une fois par joueur toutes les 5 minutes.",
            "Messagerie", Script("""
            -- Réponses automatiques aux messages privés, par mots-clés.
            -- Ce script n'a pas de move() : il écoute seulement les MP. Tu peux aussi copier
            -- onPrivateMessage (et ce qui le précède) dans ton script de récolte ou de combat.
            -- Permission « Réponse automatique » requise pour répondre.

            local REPONSES = {
                { mots = { "salut", "bonjour", "slt", "cc" }, texte = "Salut ! Je suis occupé, je te réponds plus tard." },
                { mots = { "achat", "vends", "prix" },        texte = "Pas de commerce pour le moment, désolé." },
                { mots = { "groupe", "grp", "donjon" },       texte = "Pas de groupe pour l'instant, merci quand même !" },
            }
            local DELAI_PAR_JOUEUR = 300   -- secondes : une seule réponse auto par joueur toutes les 5 min
            local derniereReponse = {}     -- pseudo -> os.time() de la dernière réponse

            -- Réponse correspondant au texte reçu (nil si aucun mot-clé).
            local function reponsePour(texte)
                local t = string.lower(texte)
                for _, r in ipairs(REPONSES) do
                    for _, mot in ipairs(r.mots) do
                        if string.find(t, mot, 1, true) then return r.texte end
                    end
                end
                return nil
            end

            function onPrivateMessage(from, text, senderId)
                notify("MP de " .. from .. " : " .. string.sub(text, 1, 120))   -- toast dans l'app
                local maintenant = os.time()
                local derniere = derniereReponse[from]
                if derniere and maintenant - derniere < DELAI_PAR_JOUEUR then return end
                local reponse = reponsePour(text)
                if not reponse then return end
                if sendPrivateMessage(from, reponse) then
                    derniereReponse[from] = maintenant
                    printMessage("Réponse auto envoyée à " .. from, "info")
                end
            end

            function stopped(reason)
                printMessage("Écoute des MP arrêtée (" .. reason .. ")", "info")
            end
            """)),

        new("boucle-imperative", "Boucle impérative minimale",
            "La fonction move() pilote tout : récolte, combat, puis carte suivante d'un circuit ; rejoint le circuit s'il en sort.",
            "Bases", Script("""
            -- Boucle impérative minimale : le moteur rappelle move() en boucle ; chaque appel récolte
            -- la carte courante, tente un combat, puis passe à la carte suivante du circuit.

            ELEMENTS_TO_GATHER = { 38 }   -- Blé
            MAX_PODS = 90
            MAX_MONSTERS = 3

            -- Sortie à prendre depuis chaque carte du circuit : ["x,y"] = direction
            local CIRCUIT = {
                ["4,-22"] = "right",
                ["5,-22"] = "bottom",
                ["5,-21"] = "left",
                ["4,-21"] = "top",
            }

            function move()
                local direction = CIRCUIT[getCurrentPos()]
                if not direction then
                    -- hors du circuit : voyage jusqu'à sa première carte (zaap / havre-sac si utile)
                    if not travelTo(4, -22) then
                        printError("Impossible de rejoindre le circuit")
                        return false   -- false = arrête le script
                    end
                    return
                end
                gatherAll()   -- récolte ce qui correspond à ELEMENTS_TO_GATHER
                fight()       -- attaque un groupe éligible s'il y en a un
                if not changeMap(direction) then
                    delay(2000)   -- sortie ratée : petite pause, move() sera rappelée
                end
            end

            function bank()
                return {
                    { map = 2320, npcBank = true },   -- banque d'Astrub (intérieur)
                }
            end
            """)),

        new("planning", "Script planning (stopped + memory:)",
            "Reprend proprement après chaque arrêt du planning : compteur de sessions dans memory:, bilan dans stopped().",
            "Planning", Script("""
            -- Script pensé pour le planning : il est arrêté et relancé souvent (fin de plage,
            -- déconnexion, anti-blocage…). L'état utile est gardé dans memory: (disque, en chaînes)
            -- et stopped(reason) dresse le bilan de chaque session.

            ELEMENTS_TO_GATHER = {}   -- vide = toutes les vraies ressources
            MAX_PODS = 90
            MIN_LIFE_PERCENT = 30
            local WEBHOOK = ""        -- URL https:// d'un webhook Discord (vide = pas d'envoi)

            local demarre = false

            function move()
                if not demarre then
                    -- memory: s'utilise dans move() (au top-level, le perso n'est pas encore identifié)
                    demarre = true
                    local n = (tonumber(memory:get("sessions", "0")) or 0) + 1
                    memory:set("sessions", n)
                    printMessage("Session n° " .. n .. " (dernier arrêt : " .. memory:get("dernierArret", "aucun") .. ")", "info")
                end
                return {
                    { map = "4,-22", gather = true, fight = true, path = "right" },
                    { map = "5,-22", gather = true, fight = true, path = "bottom" },
                    { map = "5,-21", gather = true, fight = true, path = "left" },
                    { map = "4,-21", gather = true, fight = true, path = "top" },
                }
            end

            function bank()
                return {
                    { map = 2320, npcBank = true },   -- banque d'Astrub (intérieur)
                }
            end

            -- Appelée une fois à la fin du run (budget 2 s) : pas de verbe bloquant ici,
            -- seulement print*, memory:*, sendWebhook et des lectures.
            function stopped(reason)
                memory:set("dernierArret", reason)
                local bilan = getCharacterName() .. " arrêté (" .. reason .. ") : " .. stats:harvests()
                    .. " récoltes, " .. stats:won() .. "/" .. stats:fights() .. " combats gagnés"
                if reason == "planning" or reason == "user" or reason == "ended" then
                    printSuccess(bilan)
                else
                    printWarning(bilan)   -- arrêt subi : stuck, error, disconnected…
                    if WEBHOOK ~= "" then sendWebhook(WEBHOOK, bilan) end
                end
            end
            """)),
    ];

    // Corps de script : fins de ligne \n et saut de ligne final.
    private static string Script(string body) => N(body) + "\n";
}
