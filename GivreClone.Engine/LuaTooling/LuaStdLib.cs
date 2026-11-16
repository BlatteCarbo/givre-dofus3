namespace GivreClone.Engine.LuaTooling;

/// <summary>
/// Bibliothèque standard Lua 5.2 RÉELLEMENT présente dans les scripts GivreClone : MoonSharp 2.0.0 en
/// <c>CoreModules.Preset_SoftSandbox</c> (inventaire relevé par sonde sur l'interpréteur, pas de mémoire).
/// Pas de io, ni os.execute / os.getenv / os.remove (OS_System absent), ni debug. load / loadstring / dofile /
/// require sont ajoutés par l'hôte et documentés dans lua-api-doc.json (catégorie Plateforme) : pas ici.
/// print est remplacé par l'hôte (console du bot) : décrit ici comme tel.
/// </summary>
public static class LuaStdLib
{
    public const string CategoryId = "stdlib";
    public const string CategoryTitle = "Lua standard";

    /// <summary>Tables de la bibliothèque standard exposées aux scripts (ordre d'affichage).</summary>
    public static IReadOnlyList<string> Tables { get; } =
        ["string", "table", "math", "os", "coroutine", "bit32", "json", "dynamic"];

    // Déclaré AVANT Entries : l'ordre textuel fixe l'ordre d'initialisation des statiques.
    private static readonly IReadOnlyList<string> NoBadges = [];

    public static IReadOnlyList<LuaApiEntry> Entries { get; } = Build();

    private static List<LuaApiEntry> Build()
    {
        var l = new List<LuaApiEntry>(160);

        // ---- Fonctions de base (_G) ----
        F(l, null, "assert", "v [, message]", "v, …",
            "Lève une erreur avec message si v est faux ou nil ; sinon renvoie tous ses arguments.",
            "assert(getMapId() > 0, \"carte inconnue\")");
        F(l, null, "collectgarbage", "[option [, arg]]", "nil",
            "Présente pour compatibilité : sous MoonSharp la mémoire est gérée par .NET, l'appel n'a pas d'effet utile.",
            null);
        F(l, null, "error", "message [, niveau]", null,
            "Lève une erreur Lua (rattrapable par pcall) ; niveau 1 = ta fonction, 2 = celle qui l'a appelée.",
            "error(\"banque introuvable\")");
        F(l, null, "getmetatable", "objet", "table|nil",
            "Renvoie la métatable de objet (nil s'il n'en a pas).",
            null);
        F(l, null, "ipairs", "t", "itérateur",
            "Parcourt t[1], t[2]… dans l'ordre jusqu'au premier trou (nil) : à utiliser dans for i, v in ipairs(t).",
            "for i, g in ipairs(map:getMonsters()) do print(i .. \" : \" .. g.name) end");
        F(l, null, "next", "t [, clé]", "clé, valeur",
            "Renvoie la clé suivante de t et sa valeur (nil à la fin) ; next(t) == nil teste si une table est vide.",
            "if next(liste) == nil then print(\"liste vide\") end");
        F(l, null, "pack", "…", "table",
            "Range ses arguments dans une table, avec n = leur nombre (extension MoonSharp, = table.pack).",
            "local t = pack(getPods(), getPodsMax())");
        F(l, null, "pairs", "t", "itérateur",
            "Parcourt TOUTES les clés de t, dans un ordre non garanti : for k, v in pairs(t) do … end.",
            "for id, niveau in pairs(job:all()) do print(id, niveau) end");
        F(l, null, "pcall", "f, …", "ok, résultats | erreur",
            "Appelle f(…) en mode protégé : renvoie true et ses résultats, ou false et le message d'erreur, sans arrêter le script.",
            "local ok, err = pcall(gatherAll)\nif not ok then printError(tostring(err)) end");
        F(l, null, "print", "message", null,
            "Écrit un message d'information dans la console du bot (fonction remplacée par l'hôte : seul le premier argument est affiché).",
            "print(\"Pods : \" .. getPodsPercent() .. \" %\")");
        F(l, null, "rawequal", "a, b", "bool",
            "Compare a et b sans passer par la métaméthode __eq.",
            null);
        F(l, null, "rawget", "t, clé", "valeur",
            "Lit t[clé] sans passer par la métaméthode __index.",
            null);
        F(l, null, "rawlen", "v", "number",
            "Longueur d'une table ou d'une chaîne sans passer par la métaméthode __len.",
            null);
        F(l, null, "rawset", "t, clé, valeur", "table",
            "Écrit t[clé] = valeur sans passer par la métaméthode __newindex.",
            null);
        F(l, null, "select", "n, …", "…",
            "Avec \"#\", renvoie le nombre d'arguments ; avec un nombre n, renvoie les arguments à partir du n-ième (négatif = depuis la fin).",
            "local nb = select(\"#\", ...)");
        F(l, null, "setmetatable", "t, métatable", "table",
            "Associe une métatable à t (métaméthodes __index, __call…) et renvoie t.",
            "local defauts = setmetatable({}, { __index = function() return 0 end })");
        F(l, null, "tonumber", "v [, base=10]", "number|nil",
            "Convertit une chaîne en nombre (nil si impossible), base 2 à 36 en option. Indispensable pour relire memory:get.",
            "local n = tonumber(memory:get(\"banques\", \"0\")) + 1");
        F(l, null, "tostring", "v", "string",
            "Convertit n'importe quelle valeur en texte (nil → \"nil\", true → \"true\").",
            "print(\"Carte \" .. tostring(getMapId()))");
        F(l, null, "type", "v", "string",
            "Type de v en texte : \"nil\", \"number\", \"string\", \"boolean\", \"table\", \"function\", \"thread\" ou \"userdata\".",
            "if type(ELEMENTS_TO_GATHER) == \"table\" then print(#ELEMENTS_TO_GATHER) end");
        F(l, null, "unpack", "t [, i=1 [, j=#t]]", "…",
            "Renvoie les éléments t[i]…t[j] comme valeurs séparées (= table.unpack).",
            "local x, y = unpack({ 4, -22 })");
        F(l, null, "xpcall", "f, gestionnaire [, …]", "ok, résultats | erreur",
            "Comme pcall, mais gestionnaire(erreur) est appelé pour transformer le message avant le retour.",
            "xpcall(move, function(e) return \"move : \" .. tostring(e) end)");
        V(l, null, "_G", "table", null,
            "Table de toutes les variables globales : _G[\"MAX_PODS\"] équivaut à MAX_PODS.");
        V(l, null, "_VERSION", "string", "\"MoonSharp 2.0.0.0\"",
            "Version de l'interpréteur Lua (MoonSharp, dialecte Lua 5.2).");

        // ---- string ----
        T(l, "string", "Bibliothèque de chaînes : recherche, motifs, format. S'utilise aussi en méthode : s:upper(), s:format(…).");
        F(l, "string", "byte", "s [, i=1 [, j=i]]", "number…",
            "Codes numériques des caractères s[i]…s[j].",
            "local code = string.byte(\"A\")   -- 65");
        F(l, "string", "char", "…", "string",
            "Construit une chaîne à partir de codes de caractères.",
            "string.char(72, 105)   -- \"Hi\"");
        F(l, "string", "contains", "s, texte", "bool",
            "Renvoie true si s contient texte (recherche simple, sans motif) ; extension MoonSharp.",
            "if string.contains(message, \"vends\") then return end");
        F(l, "string", "endsWith", "s, suffixe", "bool",
            "Renvoie true si s se termine par suffixe ; extension MoonSharp.",
            null);
        F(l, "string", "find", "s, motif [, début=1 [, simple=false]]", "début, fin, captures…",
            "Position de la première occurrence de motif dans s (nil si absent) ; simple = true désactive les motifs.",
            "if string.find(nom, \"Bouftou\", 1, true) then print(\"Bouftou !\") end");
        F(l, "string", "format", "format, …", "string",
            "Formate à la printf : %d entier, %s texte, %.2f décimal, %5d largeur, %x hexadécimal, %q chaîne citée.",
            "print(string.format(\"%s : %d kamas\", getCharacterName(), getKamas()))");
        F(l, "string", "gmatch", "s, motif", "itérateur",
            "Itère sur chaque occurrence de motif dans s (ou sur ses captures).",
            "for mot in string.gmatch(texte, \"%a+\") do print(mot) end");
        F(l, "string", "gsub", "s, motif, remplacement [, max]", "string, nombre",
            "Remplace les occurrences de motif (remplacement = texte avec %1…, table ou fonction) ; renvoie aussi le nombre de remplacements.",
            "local propre = string.gsub(texte, \"%s+\", \" \")");
        F(l, "string", "len", "s", "number",
            "Longueur de s en caractères (= #s).",
            null);
        F(l, "string", "lower", "s", "string",
            "Copie de s en minuscules.",
            "if string.lower(texte) == \"salut\" then print(\"coucou\") end");
        F(l, "string", "match", "s, motif [, début=1]", "captures…",
            "Première correspondance de motif dans s : ses captures, sinon la correspondance entière (nil si absente).",
            "local x, y = string.match(getCurrentPos(), \"(-?%d+),(-?%d+)\")");
        F(l, "string", "rep", "s, n [, séparateur]", "string",
            "Répète s n fois (avec un séparateur optionnel).",
            "print(string.rep(\"-\", 20))");
        F(l, "string", "reverse", "s", "string",
            "Copie de s à l'envers.",
            null);
        F(l, "string", "startsWith", "s, préfixe", "bool",
            "Renvoie true si s commence par préfixe ; extension MoonSharp.",
            "if string.startsWith(texte, \"!\") then print(\"commande\") end");
        F(l, "string", "sub", "s, i [, j=-1]", "string",
            "Sous-chaîne de i à j inclus (indices négatifs = depuis la fin).",
            "local debut = string.sub(texte, 1, 10)");
        F(l, "string", "unicode", "s [, i=1 [, j=i]]", "number…",
            "Comme byte, mais renvoie les points de code Unicode : fiable avec les lettres accentuées ; extension MoonSharp.",
            null);
        F(l, "string", "upper", "s", "string",
            "Copie de s en majuscules.",
            null);

        // ---- table ----
        T(l, "table", "Bibliothèque des tables : insertion, suppression, tri, concaténation.");
        F(l, "table", "concat", "t [, séparateur [, i=1 [, j=#t]]]", "string",
            "Colle les éléments t[i]…t[j] (chaînes ou nombres) avec un séparateur.",
            "print(table.concat({ \"Blé\", \"Orge\" }, \", \"))");
        F(l, "table", "insert", "t, [position,] valeur", null,
            "Ajoute valeur en fin de liste, ou à position en décalant la suite.",
            "table.insert(vus, getMapId())");
        F(l, "table", "pack", "…", "table",
            "Range ses arguments dans une table, avec n = leur nombre.",
            "local t = table.pack(combat:pointsLeft())");
        F(l, "table", "remove", "t [, position=#t]", "valeur",
            "Retire et renvoie l'élément à position (le dernier par défaut) en décalant la suite.",
            "local dernier = table.remove(file)");
        F(l, "table", "sort", "t [, inférieur]", null,
            "Trie la liste en place (ordre croissant, ou selon la fonction inférieur(a, b)).",
            "table.sort(ennemis, function(a, b) return a.hpPercent < b.hpPercent end)");
        F(l, "table", "unpack", "t [, i=1 [, j=#t]]", "…",
            "Renvoie les éléments t[i]…t[j] comme valeurs séparées.",
            "local x, y = table.unpack(coords)");

        // ---- math ----
        T(l, "math", "Bibliothèque mathématique : arrondis, min/max, aléatoire, trigonométrie.");
        F(l, "math", "abs", "x", "number", "Valeur absolue de x.", null);
        F(l, "math", "acos", "x", "number", "Arc cosinus de x (en radians).", null);
        F(l, "math", "asin", "x", "number", "Arc sinus de x (en radians).", null);
        F(l, "math", "atan", "x", "number", "Arc tangente de x (en radians).", null);
        F(l, "math", "atan2", "y, x", "number", "Arc tangente de y / x en tenant compte du quadrant (en radians).", null);
        F(l, "math", "ceil", "x", "number", "Plus petit entier supérieur ou égal à x (arrondi vers le haut).",
            "math.ceil(2.1)   -- 3");
        F(l, "math", "cos", "x", "number", "Cosinus de x (en radians).", null);
        F(l, "math", "cosh", "x", "number", "Cosinus hyperbolique de x.", null);
        F(l, "math", "deg", "x", "number", "Convertit x de radians en degrés.", null);
        F(l, "math", "exp", "x", "number", "Exponentielle de x (e puissance x).", null);
        F(l, "math", "floor", "x", "number", "Plus grand entier inférieur ou égal à x (arrondi vers le bas).",
            "local lots = math.floor(getInventoryItemCount(289) / 100)");
        F(l, "math", "fmod", "x, y", "number", "Reste de la division de x par y (signe de x).", null);
        F(l, "math", "frexp", "x", "m, e", "Décompose x en mantisse et exposant (x = m × 2^e).", null);
        V(l, "math", "huge", "number", "inf", "Valeur infinie (plus grande que tout nombre) : pratique comme valeur de départ d'un minimum.");
        F(l, "math", "ldexp", "m, e", "number", "Calcule la valeur m × 2^e.", null);
        F(l, "math", "log", "x [, base]", "number", "Logarithme de x (népérien par défaut, ou dans la base donnée).",
            "math.log(8, 2)   -- 3");
        F(l, "math", "max", "x, …", "number", "Plus grande des valeurs données.",
            "local n = math.max(prixMin - 1, PRIX_PLANCHER)");
        F(l, "math", "min", "x, …", "number", "Plus petite des valeurs données.",
            null);
        F(l, "math", "modf", "x", "entier, fraction", "Sépare x en partie entière et partie fractionnaire.", null);
        V(l, "math", "pi", "number", "3.14159…", "La constante π.");
        F(l, "math", "pow", "x, y", "number", "Calcule x à la puissance y (= x ^ y).", null);
        F(l, "math", "rad", "x", "number", "Convertit x de degrés en radians.", null);
        F(l, "math", "random", "[m [, n]]", "number",
            "Sans argument : réel dans [0, 1[ ; random(n) : entier de 1 à n ; random(m, n) : entier de m à n.",
            "delay(math.random(800, 1500))");
        F(l, "math", "randomseed", "x", null, "Réinitialise le générateur aléatoire avec la graine x.",
            "math.randomseed(os.time())");
        F(l, "math", "sin", "x", "number", "Sinus de x (en radians).", null);
        F(l, "math", "sinh", "x", "number", "Sinus hyperbolique de x.", null);
        F(l, "math", "sqrt", "x", "number", "Racine carrée de x.", null);
        F(l, "math", "tan", "x", "number", "Tangente de x (en radians).", null);
        F(l, "math", "tanh", "x", "number", "Tangente hyperbolique de x.", null);

        // ---- os (heure seulement : ni execute, ni fichiers) ----
        T(l, "os", "Bibliothèque du temps : os.time, os.clock, os.date, os.difftime. Pas d'accès au système (ni os.execute, ni fichiers).");
        F(l, "os", "clock", "", "number",
            "Temps processeur écoulé en secondes (avec décimales) : pratique pour chronométrer un bloc.",
            "local t0 = os.clock()");
        F(l, "os", "date", "[format [, horodatage]]", "string|table",
            "Date formatée (heure du PC) : \"%H:%M\", \"%d/%m/%Y\"… ; \"*t\" renvoie une table { year, month, day, hour, min, sec, wday… } ; préfixe \"!\" = UTC.",
            "if os.date(\"*t\").hour >= 23 then stopScript() end");
        F(l, "os", "difftime", "t2, t1", "number",
            "Différence t2 - t1 en secondes entre deux horodatages os.time().",
            null);
        F(l, "os", "time", "[date]", "number",
            "Horodatage courant en secondes ; avec une table { year, month, day, hour, min, sec }, l'horodatage de cette date.",
            "local debut = os.time()");

        // ---- coroutine ----
        T(l, "coroutine", "Bibliothèque des coroutines : fonctions suspendues et reprises (générateurs, tâches découpées).");
        F(l, "coroutine", "create", "f", "thread", "Crée une coroutine qui exécutera f (sans la démarrer).", null);
        F(l, "coroutine", "resume", "co [, …]", "ok, valeurs…",
            "Démarre ou reprend la coroutine co ; renvoie true et les valeurs passées à yield, ou false et l'erreur.", null);
        F(l, "coroutine", "running", "", "thread", "Coroutine en cours d'exécution.", null);
        F(l, "coroutine", "status", "co", "string", "État de co : \"suspended\", \"running\", \"normal\" ou \"dead\".", null);
        F(l, "coroutine", "wrap", "f", "function",
            "Crée une coroutine et renvoie une fonction qui la reprend à chaque appel (lève l'erreur au lieu de renvoyer false).",
            "local suivant = coroutine.wrap(function() for i = 1, 3 do coroutine.yield(i) end end)");
        F(l, "coroutine", "yield", "…", "…", "Suspend la coroutine courante et rend ces valeurs à resume.", null);

        // ---- bit32 ----
        T(l, "bit32", "Bibliothèque bit32 : opérations bit à bit sur des entiers 32 bits (Lua 5.2).");
        F(l, "bit32", "arshift", "x, n", "number", "Décalage arithmétique de x de n bits vers la droite (signe conservé).", null);
        F(l, "bit32", "band", "…", "number", "ET bit à bit de ses arguments.", "bit32.band(12, 10)   -- 8");
        F(l, "bit32", "bnot", "x", "number", "NON bit à bit de x.", null);
        F(l, "bit32", "bor", "…", "number", "OU bit à bit de ses arguments.", null);
        F(l, "bit32", "btest", "…", "bool", "Renvoie true si le ET bit à bit de ses arguments est non nul.", null);
        F(l, "bit32", "bxor", "…", "number", "OU exclusif bit à bit de ses arguments.", null);
        F(l, "bit32", "extract", "n, champ [, largeur=1]", "number", "Extrait les bits champ…champ+largeur-1 de n.", null);
        F(l, "bit32", "lrotate", "x, n", "number", "Rotation de x de n bits vers la gauche.", null);
        F(l, "bit32", "lshift", "x, n", "number", "Décalage de x de n bits vers la gauche.", "bit32.lshift(1, 4)   -- 16");
        F(l, "bit32", "replace", "n, v, champ [, largeur=1]", "number", "Remplace les bits champ…champ+largeur-1 de n par v.", null);
        F(l, "bit32", "rrotate", "x, n", "number", "Rotation de x de n bits vers la droite.", null);
        F(l, "bit32", "rshift", "x, n", "number", "Décalage logique de x de n bits vers la droite.", null);

        // ---- json (module MoonSharp) ----
        T(l, "json", "Bibliothèque json (module MoonSharp) : texte JSON ↔ table Lua (réponses de http:get, webhooks).");
        F(l, "json", "isnull", "v", "bool",
            "Renvoie true si v vaut nil ou la valeur null JSON (json.null()).",
            "if json.isnull(t.prix) then print(\"prix absent\") end");
        F(l, "json", "null", "", "valeur",
            "Valeur spéciale représentant null en JSON (à placer dans une table avant json.serialize).",
            null);
        F(l, "json", "parse", "texte", "table",
            "Convertit un texte JSON en table Lua (tableaux JSON = listes 1-based) ; erreur Lua si le texte est invalide.",
            "local t = json.parse(r.body)");
        F(l, "json", "serialize", "table", "string",
            "Convertit une table Lua en texte JSON (liste 1..n = tableau, sinon objet).",
            "print(json.serialize({ carte = getMapId(), pods = getPodsPercent() }))");

        // ---- dynamic (extension MoonSharp) ----
        T(l, "dynamic", "Bibliothèque dynamic (extension MoonSharp) : évaluation d'EXPRESSIONS Lua données en texte.");
        F(l, "dynamic", "eval", "expression", "valeur",
            "Évalue une expression Lua écrite dans une chaîne (ou préparée par dynamic.prepare) et renvoie sa valeur.",
            "local n = dynamic.eval(\"MAX_PODS - 5\")");
        F(l, "dynamic", "prepare", "expression", "objet",
            "Précompile une expression pour l'évaluer plusieurs fois avec dynamic.eval.",
            null);

        return l;
    }

    // Fonction (ns null = fonction de base).
    private static void F(List<LuaApiEntry> l, string? ns, string name, string args, string? returns, string desc, string? example) =>
        l.Add(new LuaApiEntry(Id(ns, name), ns, name, args, ParseArgs(args), returns, desc, example, NoBadges,
            CategoryId, CategoryTitle, LuaApiKind.StdLib));

    // Valeur (math.pi, _VERSION…) : Args null = pas appelable.
    private static void V(List<LuaApiEntry> l, string? ns, string name, string type, string? def, string desc) =>
        l.Add(new LuaApiEntry(Id(ns, name), ns, name, null, [], null, desc, null, NoBadges,
            CategoryId, CategoryTitle, LuaApiKind.StdLib, ConstType: type, ConstDefault: def));

    // Entrée de la table elle-même (Namespace null, Name = "string"…).
    private static void T(List<LuaApiEntry> l, string name, string desc) =>
        l.Add(new LuaApiEntry("std.lib." + name, null, name, null, [], null, desc, null, NoBadges,
            CategoryId, CategoryTitle, LuaApiKind.StdLib));

    private static string Id(string? ns, string name) => ns is null ? "std." + name : $"std.{ns}.{name}";

    /// <summary>Paramètres du format d'arguments de ce fichier : "s, i [, j=-1]", "t, [position,] valeur", "…".
    /// Un nom ouvert sous un crochet est optionnel ; "nom=val" porte un défaut.</summary>
    internal static IReadOnlyList<LuaParam> ParseArgs(string args)
    {
        var res = new List<LuaParam>();
        int depth = 0, i = 0;
        while (i < args.Length)
        {
            char c = args[i];
            if (c == '[') { depth++; i++; continue; }
            if (c == ']') { depth = Math.Max(0, depth - 1); i++; continue; }
            if (c == ',' || char.IsWhiteSpace(c)) { i++; continue; }
            int start = i;
            while (i < args.Length && args[i] is not (',' or '[' or ']')) i++;
            string tok = args[start..i].Trim();
            if (tok.Length == 0) continue;
            int eq = tok.IndexOf('=');
            string name = eq < 0 ? tok : tok[..eq].Trim();
            string? def = eq < 0 ? null : tok[(eq + 1)..].Trim();
            bool variadic = name is "…" or "...";
            res.Add(new LuaParam(variadic ? "…" : name, depth > 0 || variadic, def));
        }
        return res;
    }
}
