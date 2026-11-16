using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;
using GivreClone.App.Services.Mcp;
using GivreClone.App.Services.Supervisor;
using GivreControl;

namespace GivreClone.App.ViewModels;

// Outils MCP supplémentaires : comptes, journaux, métriques de combat, profil de combat, zones et expéditions, réglages (lecture).
// Tout passe par les mêmes garde-fous que les autres outils (McpAllowActions pour les écritures, comptes protégés pour les ordres).
public partial class BotsViewModel
{

    private static readonly Regex SensitiveName = new("token|key|pass|secret|licen|webhook|proxy|discord|mail|login|cert|seed|auth", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string LogsDir => Path.Combine(MZP19LocalClone.DataRoot, "logs");

    private static string? PickLog(string file)
        => TransportLogFiles.Select(LogsDir, file, TransportDiag.Path_);

    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) is not null) yield return line;
    }

    // A transport log may contain diagnostics or user-controlled messages. Never forward obvious credentials.
    // Raw transport logs still require a trusted local client; this is best-effort redaction, not a privacy proof.
    private static string SafeLog(string line) => IncidentReport.Redact(line);

    private static JsonNode? Simple(object? v) => v switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        Enum e => JsonValue.Create(e.ToString()),
        _ => null,
    };

    private static bool IsSimple(Type t) => t.IsPrimitive || t == typeof(string) || t.IsEnum;

    private static JsonObject Dump(object o, Func<string, bool>? skip = null)
    {
        var res = new JsonObject();
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || !IsSimple(p.PropertyType) || (skip?.Invoke(p.Name) ?? false)) continue;
            res[p.Name] = Simple(p.GetValue(o));
        }
        return res;
    }

    private AccountRow? AccountOf(BotSnapshot bot)
        => _accounts.Accounts.FirstOrDefault(r => bot.Pid > 0 && r.Pid == bot.Pid);

    /// <summary>Outils supplémentaires ; null = outil inconnu (l'appelant lève l'erreur habituelle).</summary>
    internal async Task<JsonNode?> ControlExtraAsync(ControlRequest request, AppBotOrders? orders, CancellationToken ct)
    {
        var a = request.Arguments;
        switch (request.Tool)
        {
            case "list_accounts":
            {
                var arr = new JsonArray();
                foreach (var r in _accounts.Accounts)
                    arr.Add(new JsonObject { ["id"] = r.Model.Id?.ToString(), ["label"] = r.Label, ["character"] = r.CharacterName ?? r.LastCharacter,
                        ["pid"] = r.Pid, ["online"] = r.Running, ["mode"] = r.IsSocketMode ? "socket" : "mitm", ["status"] = r.Status, ["blocked"] = McpGuards.IsBlocked(r.Login, AppConfig.Current.McpBlockedLogins) });
                return new JsonObject { ["accounts"] = arr, ["note"] = "Passe 'id' comme argument 'bot' pour viser un compte sans ambiguïté (deux comptes peuvent porter le même nom). blocked=true : jamais connecté ni piloté par l'automatisation." };
            }
            case "find_capturable_monsters":
            {
                var sdb = Stores.Launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                var found = sdb.CapturableMonsters(Text(a, "search"), Number(a, "limit", 30, 1, 200));
                return new JsonObject { ["monsters"] = new JsonArray(found.Select(m => (JsonNode?)new JsonObject { ["id"] = m.Id, ["name"] = m.Name }).ToArray()),
                    ["note"] = "Monstres dont les données du jeu listent une âme capturable ; vérifie en jeu que l'archimonstre visé est bien capturable." };
            }
            case "make_soul_script":
            {
                // Script de farm de capture d'âmes pour une liste d'ids de monstres (voir find_capturable_monsters) ; save=true l'enregistre.
                var sdb2 = Stores.Launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                var ids = a.TryGetProperty("monsters", out var mv) && mv.ValueKind == JsonValueKind.Array ? mv.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).Distinct().Take(40).ToList()
                    : throw new ArgumentException("monsters must be an array of monster ids.");
                var picked = ids.Select(i => (Id: i, Name: sdb2.CapturableMonsters("", 5000).FirstOrDefault(m => m.Id == i).Name ?? ("monstre " + i))).ToList();
                if (picked.Count == 0) throw new ArgumentException("No monster id given.");
                string scode = Services.Combat.SoulScript.Build(picked, Number(a, "maxPodsPercent", 90, 10, 100));
                bool ssave = a.TryGetProperty("save", out var ss) && ss.ValueKind == JsonValueKind.True;
                string sname = Text(a, "name", "Capture d'âmes");
                if (ssave) ControlScripts.Save(sname, scode, true);
                return new JsonObject { ["monsters"] = picked.Count, ["saved"] = ssave, ["name"] = ssave ? sname : null, ["code"] = scode };
            }
            case "make_gather_script":
            {
                // Génère un script de récolte (circuit de cartes) pour une ressource ; « save » l'enregistre dans les scripts (nom « Récolte <ressource> » par défaut).
                var gmap = Services.Resources.ResourceMap.Default;
                var gres = gmap.Resolve(Required(a, "resource")) ?? throw new ArgumentException("Unknown resource (name or item id).");
                uint gpid = !string.IsNullOrWhiteSpace(Text(a, "bot")) && ControlBot(a) is { Pid: > 0 } gb ? (uint)gb.Pid : 0;
                var gorigin = EcoService.OriginOf(gpid);
                bool gxy = a.TryGetProperty("x", out var gxv) && gxv.ValueKind == JsonValueKind.Number && a.TryGetProperty("y", out var gyv) && gyv.ValueKind == JsonValueKind.Number;
                var groute = Services.Resources.GatherScript.Route(gmap, gres.ItemId, gxy ? Number(a, "x", 0, -1000, 1000) : gorigin?.X, gxy ? Number(a, "y", 0, -1000, 1000) : gorigin?.Y,
                    gxy ? null : gorigin?.World, Number(a, "maps", 6, 1, 30), a.TryGetProperty("includeIndoor", out var gi) && gi.ValueKind == JsonValueKind.True);
                if (groute.Count == 0) throw new InvalidOperationException("No map recorded for this resource.");
                string gcode = Services.Resources.GatherScript.Build(gres, groute, Number(a, "maxPodsPercent", 90, 10, 100));
                bool gsave = a.TryGetProperty("save", out var gs) && gs.ValueKind == JsonValueKind.True;
                string gname = Text(a, "name", "Récolte " + gres.Name);
                if (gsave) ControlScripts.Save(gname, gcode, true);
                return new JsonObject { ["resource"] = gres.Name, ["maps"] = groute.Count, ["saved"] = gsave, ["name"] = gsave ? gname : null, ["code"] = gcode };
            }
            case "plan_production_chain":
            {
                // Chaîne « récolter → fabriquer → vendre » pour UN objet : ce qui se récolte (avec les meilleures cartes), ce qui s'achète à l'HDV, la fabrication, puis la vente (marge après taxe). Lecture seule :
                // l'IA enchaîne ensuite avec make_gather_script / travel_to / run_lua_script. Estimations d'après les prix observés par le bot (ou manuels).
                var cdb = Stores.Launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                uint cpid = !string.IsNullOrWhiteSpace(Text(a, "bot")) && ControlBot(a) is { Pid: > 0 } cb ? (uint)cb.Pid : 0;
                long cgid = Number(a, "gid", 0, 0, int.MaxValue);
                if (cgid == 0)
                {
                    string cq = Text(a, "query").Trim();
                    if (cq.Length < 2) throw new ArgumentException("Provide gid, or query (2 characters minimum).");
                    var cfound = cdb.FindItems(cq, 10);
                    var cpick = cfound.FirstOrDefault(m => cdb.Recipe(m.Gid) is not null);
                    if (cpick.Gid == 0) return new JsonObject { ["found"] = false, ["note"] = "No matching item with a recipe." };
                    cgid = cpick.Gid;
                }
                int cqty = Number(a, "quantity", 1, 1, 100000);
                var cadv = EcoService.Advice(cpid, cgid, cqty) ?? throw new InvalidOperationException("Local game database unavailable.");
                var crm = Services.Resources.ResourceMap.Default;
                var corigin = EcoService.OriginOf(cpid);
                var gather = new JsonArray(); var buy = new JsonArray();
                foreach (var x in Services.Eco.CraftAdvisor.ShoppingList(cadv.Root))
                {
                    if (crm.Resolve(x.Gid.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { } res && res.ItemId == x.Gid)
                    {
                        var hits = crm.Best(res.ItemId, corigin?.X, corigin?.Y, corigin?.World, true, 3);
                        gather.Add(new JsonObject
                        {
                            ["step"] = "gather", ["gid"] = x.Gid, ["name"] = x.Name, ["quantity"] = x.Quantity, ["job"] = Services.Resources.ResourceMap.JobName(res.Job), ["jobLevel"] = res.Level, ["buyInsteadCost"] = x.Cost,
                            ["bestMaps"] = new JsonArray(hits.Select(h => (JsonNode?)new JsonObject { ["mapId"] = h.Node.MapId, ["x"] = h.Node.X, ["y"] = h.Node.Y, ["count"] = h.Node.Count, ["distance"] = corigin is null ? null : h.Distance }).ToArray()),
                        });
                    }
                    else buy.Add(new JsonObject { ["step"] = "buy", ["gid"] = x.Gid, ["name"] = x.Name, ["quantity"] = x.Quantity, ["cost"] = x.Cost });
                }
                var crafts = new JsonArray(Services.Eco.CraftAdvisor.Flatten(cadv.Root).Where(t => t.Node.Decision == Services.Eco.CraftDecision.Craft).Select(t => (JsonNode?)new JsonObject
                    { ["step"] = "craft", ["gid"] = t.Node.Gid, ["name"] = t.Node.Name, ["quantity"] = t.Node.Quantity, ["depth"] = t.Depth }).ToArray());
                return new JsonObject
                {
                    ["found"] = true, ["gid"] = cgid, ["name"] = EcoService.NameOf(cgid), ["quantity"] = cqty,
                    ["gather"] = gather, ["buy"] = buy, ["craft"] = crafts,
                    ["sell"] = new JsonObject { ["step"] = "sell", ["craftCost"] = cadv.CraftCost, ["buyCost"] = cadv.BuyCost, ["resaleProfitAfterTax"] = cadv.ResaleProfit, ["recommendation"] = cadv.Recommendation },
                    ["order"] = "gather (travel_to + make_gather_script) -> buy at the HDV -> craft in the workshop -> sell at the HDV",
                    ["nextTools"] = new JsonArray("find_resource_maps", "make_gather_script", "travel_to", "run_lua_script", "get_market_deals"),
                    ["estimate"] = true, ["anyStalePrice"] = cadv.AnyStale,
                    ["note"] = "Plan estimé, lecture seule : rien n'est lancé. Vérifie les prix et la disponibilité des ressources avant d'enchaîner."
                };
            }
            case "find_resource_maps":
            {
                // Où récolter une ressource : sans « resource », la liste des ressources (filtrable par « job »). Avec « bot » (ou x et y) : les cartes les plus proches d'abord.
                var rmap = Services.Resources.ResourceMap.Default;
                string rq = Text(a, "resource");
                int? rjob = a.TryGetProperty("job", out var jv) && jv.ValueKind == JsonValueKind.Number ? jv.GetInt32() : null;
                if (rq.Trim().Length == 0)
                    return new JsonObject { ["resources"] = new JsonArray(rmap.Find(Text(a, "search"), rjob).Select(r => (JsonNode?)new JsonObject { ["itemId"] = r.ItemId, ["name"] = r.Name,
                        ["job"] = r.Job, ["jobName"] = Services.Resources.ResourceMap.JobName(r.Job), ["level"] = r.Level }).ToArray()) };
                var rres = rmap.Resolve(rq) ?? throw new ArgumentException("Unknown resource (name or item id); call without 'resource' to list them.");
                uint rpid = !string.IsNullOrWhiteSpace(Text(a, "bot")) && ControlBot(a) is { Pid: > 0 } rb ? (uint)rb.Pid : 0;
                var rorigin = EcoService.OriginOf(rpid);
                bool hasXy = a.TryGetProperty("x", out var hx) && hx.ValueKind == JsonValueKind.Number && a.TryGetProperty("y", out var hy) && hy.ValueKind == JsonValueKind.Number;
                int? rx = hasXy ? Number(a, "x", 0, -1000, 1000) : rorigin?.X, ry = hasXy ? Number(a, "y", 0, -1000, 1000) : rorigin?.Y;
                bool rIndoor = !(a.TryGetProperty("includeIndoor", out var ii) && ii.ValueKind == JsonValueKind.False);
                var hits = rmap.Best(rres.ItemId, rx, ry, hasXy ? null : rorigin?.World, rIndoor, Number(a, "limit", 10, 1, 100));
                return new JsonObject
                {
                    ["resource"] = rres.Name, ["itemId"] = rres.ItemId, ["job"] = Services.Resources.ResourceMap.JobName(rres.Job), ["level"] = rres.Level,
                    ["origin"] = rx is { } ox && ry is { } oy ? new JsonObject { ["x"] = ox, ["y"] = oy } : null,
                    ["maps"] = new JsonArray(hits.Select(h => (JsonNode?)new JsonObject { ["mapId"] = h.Node.MapId, ["x"] = h.Node.X, ["y"] = h.Node.Y, ["count"] = h.Node.Count,
                        ["distance"] = rx is null ? null : h.Distance, ["indoor"] = h.Node.Indoor }).ToArray()),
                    ["note"] = "Points de récolte recensés (DofusDB) : ne dit pas si la ressource est déjà récoltée ni si l'accès est possible. Distance en cartes, à vol d'oiseau."
                };
            }
            case "plan_job_xp":
            {
                // Plan hors ligne pour monter un métier : à chaque niveau la recette la moins chère par XP (mode cheapest) ou la plus rapide (fastest).
                var jdb = Stores.Launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                var job = jdb.Job(Required(a, "job")) ?? throw new ArgumentException("Unknown job (name or number).");
                int jFrom = Number(a, "fromLevel", 1, 1, 199), jTo = Number(a, "toLevel", 100, 2, 200);
                if (jTo <= jFrom) throw new ArgumentException("toLevel must be above fromLevel.");
                uint jpid = !string.IsNullOrWhiteSpace(Text(a, "bot")) && ControlBot(a) is { Pid: > 0 } jb ? (uint)jb.Pid : 0;
                bool cheapest = !string.Equals(Text(a, "mode"), "fastest", StringComparison.OrdinalIgnoreCase);
                var plan = EcoService.JobPlan(jpid, job.Id, jFrom, jTo, cheapest, Number(a, "xpBonusPercent", 100, 1, 600))
                    ?? throw new InvalidOperationException("No recipe available for this range.");
                return new JsonObject
                {
                    ["job"] = job.Name, ["fromLevel"] = jFrom, ["toLevel"] = jTo, ["mode"] = cheapest ? "cheapest" : "fastest", ["reached"] = plan.Reached,
                    ["finalLevel"] = plan.FinalLevel, ["totalCrafts"] = plan.TotalCrafts, ["totalXp"] = plan.TotalXp, ["totalCost"] = plan.TotalCost,
                    ["steps"] = new JsonArray(plan.Steps.Select(s => (JsonNode?)new JsonObject { ["fromLevel"] = s.FromLevel, ["toLevel"] = s.ToLevel, ["gid"] = s.Recipe.Gid,
                        ["recipe"] = s.Recipe.Name, ["recipeLevel"] = s.Recipe.Level, ["crafts"] = s.Crafts, ["xp"] = s.Xp, ["cost"] = s.Cost }).ToArray()),
                    ["shopping"] = new JsonArray(plan.Shopping.Select(x => (JsonNode?)new JsonObject { ["gid"] = x.Gid, ["name"] = x.Name, ["quantity"] = x.Quantity, ["cost"] = x.Cost }).ToArray()),
                    ["note"] = "Estimation : prix HDV observés ou saisis à la main (ingrédients achetés au détail) ; formules d'XP du jeu."
                };
            }
            case "get_events":
            {
                // Alertes téléphone : événements plus récents que « since » (0 = les derniers) ; waitSec (≤ 25) = long-poll jusqu'au prochain événement.
                long since = a.TryGetProperty("since", out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetInt64() : 0;
                int waitSec = Number(a, "waitSec", 0, 0, 25);
                var evs = waitSec > 0 ? await Services.PhoneAlerts.PhoneAlertHub.WaitAsync(since, TimeSpan.FromSeconds(waitSec), ct) : Services.PhoneAlerts.PhoneAlertHub.Since(since);
                return new JsonObject
                {
                    ["latest"] = Services.PhoneAlerts.PhoneAlertHub.LatestId,
                    ["events"] = new JsonArray(evs.Select(e => (JsonNode?)new JsonObject { ["id"] = e.Id, ["at"] = e.At.ToString("o"), ["kind"] = e.Kind, ["who"] = e.Who, ["text"] = e.Text }).ToArray())
                };
            }
            case "read_chat":
            {
                // Discussion du bot (tous canaux + MP envoyés) : messages plus récents que « since » ; channel = « mp », « all » ou un numéro de canal.
                var chatBot = ControlBot(a);
                if (chatBot.Pid <= 0) throw new InvalidOperationException("Bot is offline; no chat available.");
                var buf = Stores.Launch.ChatOf((uint)chatBot.Pid);
                long chatSince = a.TryGetProperty("since", out var cs) && cs.ValueKind == JsonValueKind.Number ? cs.GetInt64() : 0;
                var msgs = buf.Since(chatSince, Number(a, "limit", 100, 1, 400), Text(a, "channel", "all"));
                return new JsonObject
                {
                    ["cursor"] = buf.LatestId,
                    ["messages"] = new JsonArray(msgs.Select(m => (JsonNode?)new JsonObject { ["id"] = m.Id, ["time"] = m.At.ToString("o"), ["channel"] = m.Channel, ["label"] = m.Label, ["sender"] = m.Sender, ["text"] = m.Content, ["outgoing"] = m.Outgoing }).ToArray()),
                    ["note"] = "Seul le canal 10 (MP) est identifié avec certitude ; les autres canaux sont numérotés."
                };
            }
            case "send_private_message":
            {
                var pmBot = ControlBot(a);
                if (pmBot.Pid <= 0) throw new InvalidOperationException("Bot is offline.");
                string target = Required(a, "target").Trim(), text = Required(a, "text");
                if (target.Length > 40 || text.Length > 250) throw new ArgumentException("Target max 40 chars, text max 250.");
                bool sent = Stores.Launch.SendPrivateMessage((uint)pmBot.Pid, target, text);
                return new JsonObject { ["ok"] = sent, ["status"] = sent ? "sent" : "refused", ["serverAcknowledged"] = false };
            }
            case "list_combat_journals":
            {
                // Derniers combats (replay) : issue, durée, tours, sorts, soucis et premiers constats. « bot » filtre ; lostOnly = défaites seulement.
                uint? jpid = !string.IsNullOrWhiteSpace(Text(a, "bot")) ? (ControlBot(a) is { Pid: > 0 } jbot ? (uint)jbot.Pid : throw new InvalidOperationException("Bot is offline: its fights are listed by pid only while it runs.")) : null;
                bool lostOnly = a.TryGetProperty("lostOnly", out var lo) && lo.ValueKind == JsonValueKind.True;
                var list = Services.Combat.CombatJournalService.List(jpid, 200).Where(j => !lostOnly || j.Lost).Take(Number(a, "limit", 20, 1, 100)).ToList();
                return new JsonObject
                {
                    ["fights"] = new JsonArray(list.Select(j => (JsonNode?)new JsonObject
                    {
                        ["id"] = j.Id, ["pid"] = j.Pid, ["start"] = j.Start.ToString("o"), ["outcome"] = j.Outcome, ["durationSec"] = Math.Round(j.DurationSec, 1), ["turns"] = j.Turns,
                        ["casts"] = j.Casts, ["moves"] = j.Moves, ["problems"] = j.Problems, ["xp"] = j.Xp, ["longestGapSec"] = Math.Round(j.LongestGapSec, 1),
                        ["findings"] = new JsonArray(j.Findings.Take(4).Select(x => (JsonNode?)x).ToArray())
                    }).ToArray()),
                    ["note"] = "Depuis le lancement de l'application (et les fichiers du dossier combat/ pour les plus anciens). get_combat_journal(id) donne la chronologie complète."
                };
            }
            case "get_combat_journal":
            {
                var gj = Services.Combat.CombatJournalService.Get(Required(a, "id")) ?? throw new ArgumentException("Unknown combat id; use list_combat_journals.");
                string txt = Services.Combat.CombatJournalFormat.ToText(gj);
                int maxChars = Number(a, "maxChars", 40000, 2000, 200000);
                bool cut = txt.Length > maxChars;
                return new JsonObject { ["id"] = gj.Id, ["outcome"] = gj.Outcome, ["findings"] = new JsonArray(gj.Findings.Select(x => (JsonNode?)x).ToArray()),
                    ["text"] = cut ? txt[..maxChars] + "\n… (tronqué)" : txt, ["truncated"] = cut };
            }
            case "make_treasure_script":
            {
                // Script de chasse au trésor automatique (résolveur d'indices DofusDB) ; save=true l'enregistre (nom par défaut « Chasse au trésor »).
                string tcode = Services.Treasure.TreasureScript.Build(Number(a, "level", 0, 0, 200), Number(a, "maxFails", 3, 1, 10));
                bool tsave = a.TryGetProperty("save", out var ts) && ts.ValueKind == JsonValueKind.True;
                string tname = Text(a, "name", "Chasse au trésor");
                if (tsave) ControlScripts.Save(tname, tcode, true);
                return new JsonObject { ["saved"] = tsave, ["name"] = tsave ? tname : null, ["code"] = tcode,
                    ["note"] = "Lance-le sur un bot placé sur la carte d'un coffre de chasse. Les phorreurs n'ont pas de résolveur (avance carte par carte). À surveiller les premières fois." };
            }
            case "find_treasure_clue":
            {
                // Chasse au trésor : depuis (x, y) dans une direction, où se trouve l'indice « clue » ? Sans « clue » : la liste des indices possibles.
                int tx = Number(a, "x", 0, -1000, 1000), ty = Number(a, "y", 0, -1000, 1000);
                if (!a.TryGetProperty("x", out _) || !a.TryGetProperty("y", out _)) throw new ArgumentException("Missing argument: x and y.");
                if (!Services.Treasure.TreasureClues.TryDirection(Text(a, "direction"), out int tdir)) throw new ArgumentException("direction must be right|bottom|left|top (or 0|2|4|6).");
                var clues = await Services.Treasure.TreasureClueService.LoadAsync(tx, ty, tdir, ct);
                string wanted = Text(a, "clue");
                var launchT = Stores.Launch;
                if (wanted.Trim().Length == 0)
                    return new JsonObject { ["x"] = tx, ["y"] = ty, ["direction"] = Services.Treasure.TreasureClues.DirectionName(tdir), ["clues"] = new JsonArray(Services.Treasure.TreasureClues.Distinct(clues)
                        .Select(c => (JsonNode?)new JsonObject { ["name"] = c.Name, ["nearestX"] = c.X, ["nearestY"] = c.Y, ["distance"] = c.Distance }).ToArray()) };
                var found = Services.Treasure.TreasureClues.Matches(clues, wanted);
                return new JsonObject
                {
                    ["found"] = found.Count > 0, ["x"] = tx, ["y"] = ty, ["direction"] = Services.Treasure.TreasureClues.DirectionName(tdir),
                    ["maps"] = new JsonArray(found.Select(c => (JsonNode?)new JsonObject { ["x"] = c.X, ["y"] = c.Y, ["distance"] = c.Distance, ["name"] = c.Name, ["mapId"] = launchT.MapIdAt(c.X, c.Y) }).ToArray()),
                    ["note"] = "La première carte est la plus proche : c'est là que se trouve l'indice. Source DofusDB (mise en cache)."
                };
            }
            case "create_character":
            case "delete_character":
            {
                // Comptes hors ligne aussi : 'bot' = id ou libellé du compte (list_accounts). Socket uniquement ; compte protégé refusé.
                string key = Required(a, "bot");
                var row = _accounts.Accounts.FirstOrDefault(r => string.Equals(r.Model.Id?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                       ?? _accounts.Accounts.FirstOrDefault(r => string.Equals(r.Label, key, StringComparison.OrdinalIgnoreCase));
                if (row is null) throw new ArgumentException("Account not found; use list_accounts and an exact account id.");
                if (McpGuards.IsBlocked(row.Login, AppConfig.Current.McpBlockedLogins)) throw new InvalidOperationException("Protected account: never driven by automation.");
                if (request.Tool == "delete_character" && !(a.TryGetProperty("confirm", out var cf) && cf.ValueKind == JsonValueKind.True))
                    throw new ArgumentException("Deleting a character is irreversible: pass confirm=true only after the user explicitly asked for this exact character.");
                OrderResult res;
                if (request.Tool == "create_character")
                {
                    Dictionary<string, string>? colors = null;
                    if (a.TryGetProperty("colors", out var cj) && cj.ValueKind == JsonValueKind.Object)
                        colors = cj.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Number ? "#" + (p.Value.GetInt32() & 0xFFFFFF).ToString("X6") : p.Value.GetString() ?? "");
                    int? Rank(string k) => a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
                    var spec = new CharacterSpec(Text(a, "server"), Text(a, "breed"), Text(a, "sex"), Text(a, "name"), Rank("head"), Rank("body"), colors,
                                                 !(a.TryGetProperty("leaveTutorial", out var lt) && lt.ValueKind == JsonValueKind.False));
                    res = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _accounts.OrderCreateCharacterAsync(row, spec));
                }
                else
                    res = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _accounts.OrderDeleteCharacterAsync(row, Text(a, "server"), Text(a, "character"), Text(a, "answer")));
                return new JsonObject { ["ok"] = res.Ok, ["reason"] = res.Reason, ["note"] = "Ordre accepté = session lancée ; le résultat (créé / supprimé / refusé) est dans read_log." };
            }
            case "read_log":
            {
                string? path = PickLog(Text(a, "file"));
                if (path is null) return new JsonObject { ["file"] = null, ["lines"] = new JsonArray(), ["note"] = "aucun journal transport" };
                int tail = Number(a, "tail", 200, 1, 2000);
                string pat = Text(a, "pattern");
                if (pat.Length > 256) throw new ArgumentException("Log pattern is too long.");
                Regex? rx = pat.Length == 0 ? null : new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                var buf = new Queue<string>(tail + 1);
                // Transport logs rotate at 25 MiB. Scan the entire selected file so
                // a long session still returns its newest matching events.
                foreach (var l in ReadSharedLines(path))
                {
                    ct.ThrowIfCancellationRequested();
                    string safe = SafeLog(l);
                    if (rx is not null && !rx.IsMatch(safe)) continue;
                    buf.Enqueue(safe.Length > 600 ? safe[..600] + "…" : safe);
                    if (buf.Count > tail) buf.Dequeue();
                }
                return new JsonObject { ["file"] = Path.GetFileName(path), ["lines"] = new JsonArray(buf.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                    ["scanTruncated"] = false, ["redactionBestEffort"] = true };
            }
            case "fight_metrics":
            {
                string? path = PickLog(Text(a, "file"));
                if (path is null) return new JsonObject { ["file"] = null, ["note"] = "aucun journal transport" };
                var rep = FightLogMetrics.Analyze(ReadSharedLines(path));
                var fights = new JsonArray();
                foreach (var f in rep.Fights.TakeLast(Number(a, "last", 20, 1, 100)))
                    fights.Add(new JsonObject { ["rounds"] = f.Rounds, ["gapsMs"] = new JsonArray(f.GapsMs.Select(g => (JsonNode?)JsonValue.Create(g)).ToArray()), ["foreignAcks"] = f.ForeignAcks });
                return new JsonObject { ["file"] = Path.GetFileName(path), ["medianGapMs"] = rep.MedianGapMs, ["maxGapMs"] = rep.MaxGapMs, ["shareUnder400ms"] = Math.Round(rep.ShareUnder400Ms, 3),
                    ["fights"] = fights, ["gapMeaning"] = "ms entre le dernier marqueur 'tour prêt' (fin de mon tour) et le début de mon tour suivant : tours des monstres compris." };
            }
            case "get_combat_profile":
            {
                var bot = ControlBot(a);
                if (string.IsNullOrWhiteSpace(bot.Name)) throw new ArgumentException("Personnage inconnu (bot hors ligne ?).");
                return new JsonObject { ["character"] = bot.Name, ["profile"] = Dump(CombatProfileStore.Get(bot.Name)) };
            }
            case "set_combat_profile":
            {
                var bot = ControlBot(a);
                if (string.IsNullOrWhiteSpace(bot.Name)) throw new ArgumentException("Personnage inconnu (bot hors ligne ?).");
                ControlWriteTarget(a);
                if (!a.TryGetProperty("changes", out var ch) || ch.ValueKind != JsonValueKind.Object) throw new ArgumentException("Argument 'changes' (objet) requis.");
                if (ch.EnumerateObject().Count() is 0 or > 32) throw new ArgumentException("Expected 1..32 combat profile changes.");
                var prof = CombatProfileStore.Get(bot.Name);
                var applied = new JsonArray();
                var pending = new List<Action>();
                var distinct = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in ch.EnumerateObject())
                {
                    if (!distinct.Add(kv.Name)) throw new ArgumentException("Duplicate combat profile property.");
                    var p = prof.GetType().GetProperty(kv.Name, BindingFlags.Public | BindingFlags.Instance);
                    if (p is null || !p.CanWrite || !IsSimple(p.PropertyType)
                        || p.Name == nameof(CombatProfile.ChallengeByScript))
                        throw new ArgumentException("Unknown or unsupported combat profile property.");
                    object v;
                    try
                    {
                        v = p.PropertyType.IsEnum ? Enum.Parse(p.PropertyType, kv.Value.GetString() ?? "", true)
                            : p.PropertyType == typeof(bool) ? kv.Value.GetBoolean()
                            : p.PropertyType == typeof(int) ? kv.Value.GetInt32()
                            : p.PropertyType == typeof(long) ? kv.Value.GetInt64()
                            : p.PropertyType == typeof(double) ? kv.Value.GetDouble()
                            : kv.Value.GetString() ?? "";
                    }
                    catch (Exception) { throw new ArgumentException("Invalid combat profile property type or value."); }
                    if (p.PropertyType.IsEnum && (!Enum.IsDefined(p.PropertyType, v) || int.TryParse(kv.Value.GetString(), out _)))
                        throw new ArgumentException("Unknown combat profile enum value.");
                    if (v is int number)
                    {
                        int max = p.Name switch
                        {
                            nameof(CombatProfile.MaxCastsPerTurn) => 99,
                            nameof(CombatProfile.KiteMinDistance) or nameof(CombatProfile.KiteMaxDistance) => 50,
                            nameof(CombatProfile.ChallengeAbandonHp) or nameof(CombatProfile.FleeBelowHpPercent) => 100,
                            _ => 100000
                        };
                        int min = p.Name == nameof(CombatProfile.MaxCastsPerTurn) ? 1 : 0;
                        if (number < min || number > max) throw new ArgumentException("Combat profile integer outside supported limits.");
                    }
                    pending.Add(() => p.SetValue(prof, v));
                    applied.Add(kv.Name);
                }
                ct.ThrowIfCancellationRequested();
                foreach (var write in pending) write();
                CombatProfileStore.Save();
                if (bot.Pid > 0) Stores.Launch.SetCombatProfile((uint)bot.Pid, prof);
                return new JsonObject { ["applied"] = applied, ["ignored"] = new JsonArray(),
                    ["persistenceVerified"] = false, ["profile"] = Dump(prof) };
            }
            case "get_alerts":
            {
                // Points d'attention par bot (session perdue / morte, pods presque pleins, PV bas hors combat) : mêmes règles que la Vue d'ensemble.
                var arr = new JsonArray();
                foreach (var b in Bots.ToArray())
                {
                    string name = b.Row.CharacterName ?? "";
                    if (b.Row.Activity is BotActivity.Disconnected or BotActivity.Dead)
                    { arr.Add(new JsonObject { ["bot"] = name, ["category"] = "Session", ["detail"] = b.Row.ActivityText }); continue; }
                    if (b.Pid <= 0) continue;
                    foreach (var (category, detail) in Services.Signals.BotSignals.For(new Services.Signals.BotVitals(b.CharLife, b.CharMaxLife, b.PodsCur, b.PodsMax, Stores.Launch.IsInFight((uint)b.Pid))))
                        arr.Add(new JsonObject { ["bot"] = name, ["category"] = category, ["detail"] = detail });
                }
                return new JsonObject { ["count"] = arr.Count, ["alerts"] = arr };
            }
            case "advise_rune":
            {
                var lines = new List<Services.Forgemagie.ForgeLine>();
                using (var ld = JsonDocument.Parse(Text(a, "linesJson") is { Length: > 0 } lj ? lj : "[]"))
                    foreach (var e in ld.RootElement.EnumerateArray())
                    {
                        int I(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                        lines.Add(new Services.Forgemagie.ForgeLine(e.TryGetProperty("stat", out var st) ? st.GetString() ?? "" : "", I("current"), I("min"), I("max"), I("target"), I("cap"), I("paFrom"), I("raFrom")));
                    }
                var stock = new Dictionary<(string, string), int>();
                using (var sd = JsonDocument.Parse(Text(a, "stockJson") is { Length: > 0 } sj ? sj : "{}"))
                    foreach (var stat in sd.RootElement.EnumerateObject())
                        foreach (var tier in stat.Value.EnumerateObject()) if (tier.Value.ValueKind == JsonValueKind.Number) stock[(stat.Name, tier.Name)] = tier.Value.GetInt32();
                bool Flag(string k, bool d) => a.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : d;
                var adv = Services.Forgemagie.RuneAdvisor.Advise(lines, stock, new Services.Forgemagie.AdvisorSettings(Flag("allowPa", true), Flag("allowRa", true), Flag("allowOver", false)));
                return new JsonObject { ["kind"] = adv.Kind.ToString(), ["stat"] = adv.Stat, ["tier"] = adv.Rune?.Tier, ["gain"] = adv.Rune?.Gain, ["reason"] = adv.Reason };
            }
            case "recommend_zones":
            {
                // Assistant de progression : sous-zones de combat adaptées à un niveau (monstres de niveau ~ celui du perso à +25 %), avec cartes et monstres. Estimation sur les données du jeu ; lecture seule.
                var zdb = Stores.Launch.GameData ?? throw new InvalidOperationException("Local game database unavailable.");
                int zlevel = Number(a, "level", 0, 0, 200);
                if (zlevel == 0 && !string.IsNullOrWhiteSpace(Text(a, "bot")) && ControlBot(a) is { Pid: > 0 } zb) zlevel = zb.Level;
                if (zlevel < 1) throw new ArgumentException("Give level, or a connected bot.");
                var zones = new List<Services.Progression.ZoneInfo>();
                foreach (var ar in zdb.ExpeditionAreas())
                    foreach (var sa in zdb.ExpeditionSubAreas(ar.Id))
                        if (sa.MonsterCount > 0)
                            zones.Add(new Services.Progression.ZoneInfo(sa.Id, ar.Name, sa.Name, zdb.ExpeditionMonsters(sa.Id).Where(m => m.MinLevel > 0).Select(m => (m.MinLevel, m.MaxLevel)).ToList(), zdb.ExpeditionMaps(sa.Id).Count));
                var ranked = Services.Progression.ZoneAdvisor.Rank(zones, zlevel, Number(a, "limit", 10, 1, 50), a.TryGetProperty("includeHard", out var ih) && ih.ValueKind == JsonValueKind.True);
                return new JsonObject
                {
                    ["level"] = zlevel,
                    ["zones"] = new JsonArray(ranked.Select(r => (JsonNode?)new JsonObject { ["subArea"] = r.Zone.SubAreaId, ["area"] = r.Zone.Area, ["name"] = r.Zone.Name, ["averageMonsterLevel"] = r.AverageLevel,
                        ["monsterKinds"] = r.Monsters, ["maps"] = r.Zone.MapCount, ["fit"] = r.Fit.ToString(), ["score"] = r.Score }).ToArray()),
                    ["next"] = "list_expedition_zones(subArea) for the monsters, then start_expedition",
                    ["note"] = "Estimation d'après les niveaux des monstres ; ne mesure ni résistances ni dégâts réels. Vérifie avec fight_metrics après quelques combats."
                };
            }
            case "set_spell_variant":
            {
                var vbot = ControlBot(a);
                if (vbot.Pid <= 0) throw new InvalidOperationException("Bot is offline.");
                uint vpid = (uint)vbot.Pid;
                int want = Number(a, "spellId", 0, 0, int.MaxValue);
                var vl = Stores.Launch;
                if (want == 0)
                {
                    var list = new JsonArray();
                    foreach (int id in vl.LearnedSpells(vpid))
                        if (vl.GameNames.SpellVariantPair(id).FirstOrDefault(i => i != id) is int alt and > 0)
                            list.Add(new JsonObject { ["spellId"] = id, ["name"] = vl.GameNames.SpellName(id), ["otherVariantId"] = alt, ["otherVariantName"] = vl.GameNames.SpellName(alt) });
                    return new JsonObject { ["spellsWithVariant"] = list };
                }
                bool vok = vl.SpellVariantSwitchAsync(vpid, want).GetAwaiter().GetResult();
                return new JsonObject { ["spellId"] = want, ["name"] = vl.GameNames.SpellName(want), ["active"] = vok, ["note"] = vok ? "variant active" : "refused: in combat, unknown variant or no fight permission" };
            }
            case "estimate_spell_damage":
            {
                // Estimation (jamais exacte) des dégâts d'un sort ou de toute la rotation du bot : dés du sort + caractéristiques du perso (force/intel/chance/agi, % dommages, dommages fixes, critique)
                // contre une cible optionnelle (monstre : résistances et PV du grade le plus proche de 'level'). Lecture seule.
                var ebot = ControlBot(a);
                if (ebot.Pid <= 0) throw new InvalidOperationException("Bot is offline.");
                uint epid = (uint)ebot.Pid;
                var launch2 = Stores.Launch;
                var chars = launch2.SelfCharacteristics(epid);
                DamageTargetHolder? holder = null;
                int emonster = Number(a, "monsterId", 0, 0, int.MaxValue);
                if (emonster > 0)
                {
                    var gs = launch2.GameData?.MonsterGradeStats(emonster, Number(a, "level", 0, 0, 400)) ?? throw new ArgumentException("Unknown monster.");
                    var rp = new Dictionary<Services.Combat.DamageElement, int>();
                    foreach (var kv in gs.ResistByElement) rp[Services.Combat.DamageEstimator.ElementOf(kv.Key)] = kv.Value;
                    holder = new DamageTargetHolder(new Services.Combat.DamageTarget(rp, null, gs.Life), gs.Level, gs.Life);
                }
                IEnumerable<int> spellIds = a.TryGetProperty("spellId", out var esv) && esv.ValueKind == JsonValueKind.Number ? new[] { esv.GetInt32() }
                    : (launch2.EffectiveCombatProfile(epid)?.Spells ?? CombatProfileStore.Get(ebot.Name).Spells).Where(sp => sp.Enabled && sp.SpellId > 0).Select(sp => sp.SpellId).Distinct();
                var rows = new JsonArray();
                foreach (int sid in spellIds)
                {
                    if (launch2.SpellDamageOf(epid, sid) is not { Max: > 0 } d) { rows.Add(new JsonObject { ["spellId"] = sid, ["name"] = launch2.GameNames.SpellName(sid), ["note"] = "no elemental damage (buff/utility) or spell data unavailable" }); continue; }
                    var est = Services.Combat.DamageEstimator.Estimate(d.Min, d.Max, Services.Combat.DamageEstimator.ElementOf(d.Element), chars, holder?.Target);
                    rows.Add(new JsonObject
                    {
                        ["spellId"] = sid, ["name"] = launch2.GameNames.SpellName(sid), ["element"] = est.Element.ToString(), ["baseDice"] = d.Min + "-" + d.Max,
                        ["min"] = est.Min, ["max"] = est.Max, ["average"] = est.Average, ["expectedWithCrit"] = est.AverageWithCrit, ["critChancePercent"] = est.CritChancePercent,
                        ["hitsToKill"] = est.HitsToKill, ["maxCastPerTurn"] = d.MaxPerTurn > 0 ? d.MaxPerTurn : null,
                    });
                }
                return new JsonObject
                {
                    ["character"] = ebot.Name, ["characteristicsKnown"] = chars.Count > 0, ["spells"] = rows,
                    ["target"] = holder is null ? null : new JsonObject { ["monsterId"] = emonster, ["gradeLevel"] = holder.Level, ["life"] = holder.Life },
                    ["estimate"] = true,
                    ["note"] = chars.Count == 0 ? "Characteristics not seen yet (reconnect the character): estimates use zero stats." : "Estimate: ignores spell-specific modifiers, power, states and special effects."
                };
            }
            case "list_expedition_zones":
            {
                var db = Stores.Launch.GameData ?? throw new InvalidOperationException("Base de données du jeu introuvable.");
                string sub = Text(a, "subArea"), area = Text(a, "area");
                if (sub.Length > 0)
                {
                    int sid = int.TryParse(sub, out var s0) ? s0 : db.ExpeditionAreas().SelectMany(x => db.ExpeditionSubAreas(x.Id)).FirstOrDefault(s => s.Name.Contains(sub, StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
                    if (sid == 0) throw new ArgumentException("Sous-zone introuvable.");
                    var ms = new JsonArray(db.ExpeditionMonsters(sid).Select(m => (JsonNode?)new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["level"] = m.LevelText }).ToArray());
                    return new JsonObject { ["subArea"] = sid, ["mapCount"] = db.ExpeditionMaps(sid).Count, ["monsters"] = ms };
                }
                if (area.Length > 0)
                {
                    var ar = db.ExpeditionAreas().FirstOrDefault(x => x.Id.ToString(CultureInfo.InvariantCulture) == area || x.Name.Contains(area, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException("Zone introuvable.");
                    return new JsonObject { ["area"] = ar.Name, ["subAreas"] = new JsonArray(db.ExpeditionSubAreas(ar.Id).Select(s => (JsonNode?)new JsonObject { ["id"] = s.Id, ["name"] = s.Name, ["level"] = s.Level, ["monsters"] = s.MonsterCount }).ToArray()) };
                }
                return new JsonObject { ["areas"] = new JsonArray(db.ExpeditionAreas().Select(x => (JsonNode?)new JsonObject { ["id"] = x.Id, ["name"] = x.Name }).ToArray()) };
            }
            case "start_expedition":
            {
                if (orders is null) throw new InvalidOperationException("Bot directory is not ready.");
                var db = Stores.Launch.GameData ?? throw new InvalidOperationException("Base de données du jeu introuvable.");
                int sid = a.GetProperty("subArea").GetInt32();
                // Match the Expedition UI: raw sub-area membership includes
                // houses and underground maps sharing exterior coordinates.
                var launch = Stores.Launch;
                var maps = ExpeditionScript.CleanMaps(db.ExpeditionMaps(sid)
                    .Select(id => (id, launch.MapCoordOf(id) is (int x, int y) ? ((int, int)?)(x, y) : null, launch.WorldMapOf(id))).ToList(), launch.ExpeditionNavigationMap, launch.ExpeditionWalkScore);
                if (maps.Count == 0) throw new ArgumentException("Sous-zone sans carte.");
                var bot = ControlBot(a);
                (int X, int Y)? start = bot.MapId > 0 ? Stores.Launch.MapCoordOf(bot.MapId) : null;
                var order = ExpeditionScript.NearestOrder(maps.Select(id => (id, Stores.Launch.MapCoordOf(id) is (int x, int y) ? ((int, int)?)(x, y) : null)).ToList(), start);
                static string Ids(JsonElement a, string k) => a.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Array ? string.Join(" ", e.EnumerateArray().Select(v => v.GetInt64())) : "";
                static int N(JsonElement a, string k, int d) => a.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : d;
                var warnings = new List<string>();
                string code = ExpeditionScript.Build(new ExpeditionProfile(db.SubArea(sid)?.Name ?? "Zone", string.Join("\n", order), N(a, "minMonsters", 1), N(a, "maxMonsters", 8),
                    Ids(a, "mandatory"), Ids(a, "forbidden"), "", N(a, "maxFights", 0), N(a, "maxMinutes", 0), N(a, "stopPods", 95),
                    ContinueAfterDeath: a.TryGetProperty("continueAfterDeath", out var cad) && cad.ValueKind == JsonValueKind.True,
                    MaxConsecutiveDefeats: Math.Max(0, N(a, "maxDefeats", 3))), warnings);
                if (SavedLuaScripts.SyntaxError(code) is { } problem) throw new ArgumentException(problem);
                var res = await orders.StartScriptAsync(Required(a, "bot"), code + "\n", ct);
                var o = ControlResult(res); o["warnings"] = new JsonArray(warnings.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray()); o["maps"] = order.Count;
                return o;
            }
            case "get_settings":
                return new JsonObject { ["settings"] = Dump(AppConfig.Current, n => SensitiveName.IsMatch(n)), ["note"] = "Lecture seule ; les champs sensibles (jetons, licences, proxys, identifiants) sont omis." };
            default: return null;
        }
    }
}

internal sealed record DamageTargetHolder(Services.Combat.DamageTarget Target, int Level, int Life);
