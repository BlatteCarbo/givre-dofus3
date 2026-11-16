using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Lot E — petites forges : verrous de combat (FIGHT_LOCK_* / combat:lock), point de respawn (map:saveZaap), refus auto
// des échanges/défis (IGNORE_REQUEST_*), variantes de sort, abandon (combat:giveUp), popup de modération
// (lastModeratorMessage / MODERATOR_WEBHOOK / MODERATOR_STOP), callback stopped(reason).
// Verbes liés AVANT DoString (Bind -> BindLotE). Lua UNIQUEMENT sur le thread du script ; les handlers d'événements
// moteur (thread réseau) ne font que journaliser (Emit est thread-safe) et poser des drapeaux — jamais d'appel Lua.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private static long s_runSeq;                 // RunId global des RunLoop (politique de script propriétaire)
    private long _loopRunId;                      // RunId du RunLoop en cours sur CET hôte (0 = aucun)
    private ScriptPolicy? _lastPolicy;            // dernière politique poussée (n'appelle le moteur que sur changement)
    private volatile string? _modWebhook;         // MODERATOR_WEBHOOK du run courant (relu à chaque itération)
    private int _modStopPending;                  // 1 = popup modérateur reçu pendant le run (MODERATOR_STOP le traite en tête de boucle)
    private readonly Action<uint, IReadOnlyList<int>> _onFightLocks;
    private readonly Action<uint, string, long> _onAutoRefused;
    private readonly Action<uint, LaunchService.ModeratorPopup> _onModPopup;
    private const int StoppedBudgetMs = 2000;     // budget du callback stopped(reason)
    private const int SpellVariantTimeoutMs = 3000;
    private const int GiveUpTimeoutMs = 6000;
    private const int RespawnSaveTimeoutMs = 4000;

    /// <summary>Script arrêté par MODERATOR_STOP après un popup de modération : l'hôte (BotsViewModel) met le planning en
    /// pause pour ce perso, comme un arrêt manuel.</summary>
    public event Action? ModeratorStopped;

    private bool LoopActive => Interlocked.Read(ref _loopRunId) != 0;

    // ------------------------------------------------------------------ politique du script (thread du script)
    private static bool ReadBool(Script s, string key, bool fb)
    {
        var v = s.Globals.Get(key);
        return v.Type switch
        {
            DataType.Boolean => v.Boolean,
            DataType.Number => v.Number != 0,
            DataType.String => v.String.Trim() is "1" || v.String.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => fb,
        };
    }

    private static string? ReadStr(Script s, string key)
    {
        var v = s.Globals.Get(key);
        return v.Type == DataType.String && !string.IsNullOrWhiteSpace(v.String) ? v.String.Trim() : null;
    }

    private void PushPolicy(Script s, long runId)
    {
        var p = new ScriptPolicy(runId,
            ReadBool(s, "FIGHT_LOCK_SPEC", false), ReadBool(s, "FIGHT_LOCK_JOIN", false), ReadBool(s, "FIGHT_LOCK_PARTY", false),
            ReadBool(s, "IGNORE_REQUEST_EXCHANGE", false), ReadBool(s, "IGNORE_REQUEST_DUEL", false),
            ReadBool(s, "IGNORE_REQUEST_GUILD", false));   // compat tiers: refus auto des invitations de guilde
        if (p != _lastPolicy) { _lastPolicy = p; _launch.SetScriptPolicy(_pid, p); }
    }

    // ------------------------------------------------------------------ callback stopped(reason)
    // Appelé UNE fois à la fin d'un RunLoop, sur le thread du script. La CTS est annulée d'abord : tout verbe BLOQUANT lève
    // aussitôt (seuls print*, sendWebhook, memory:*, leaveDialog et les lectures sont utiles). Coroutine préemptive (MoonSharp
    // 2.0, sans réflexion) : une boucle Lua pure est coupée au bout du budget.
    private void InvokeStopped(Script script, string reason, CancellationTokenSource cts)
    {
        DynValue fn;
        try { fn = script.Globals.Get("stopped"); } catch { return; }
        if (fn.Type != DataType.Function) return;
        try { cts.Cancel(); } catch { /* déjà disposé */ }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var co = script.CreateCoroutine(fn);
            co.Coroutine.AutoYieldCounter = 1000;
            var r = co.Coroutine.Resume(DynValue.NewString(reason));
            while (r.Type == DataType.YieldRequest && sw.ElapsedMilliseconds < StoppedBudgetMs) r = co.Coroutine.Resume();
            if (r.Type == DataType.YieldRequest) Warn("script", Loc.Fmt("L.Lua.StoppedCallbackTimeout", StoppedBudgetMs));
        }
        catch (Exception ex)
        {
            if (Root(ex) is not ScriptStopException) ScriptErr("script", ex, d => Loc.Fmt("L.Lua.StoppedCallbackError", d), LuaLogLevel.Warning);
        }
    }

    // ------------------------------------------------------------------ événements moteur (thread réseau : AUCUN Lua)
    private void OnFightLocksApplied(uint pid, IReadOnlyList<int> opts)
    {
        if (pid != _pid || !LoopActive) return;
        var labels = opts.Select(o => o switch
        {
            0 => Loc.T("L.Lua.FightLockSpec"),
            2 => Loc.T("L.Lua.FightLockJoin"),
            _ => Loc.T("L.Lua.FightLockParty"),
        });
        Info("combat", Loc.Fmt("L.Lua.FightLockApplied", string.Join(", ", labels)));
    }

    private void OnRequestAutoRefused(uint pid, string kind, long src)
    {
        if (pid != _pid || !LoopActive) return;
        string who;
        try { who = ResolveCharacterName(src); } catch { who = ""; }   // lecture d'état best-effort depuis le thread réseau
        if (string.IsNullOrEmpty(who)) who = src.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (kind == "duel") Info("combat", Loc.Fmt("L.Lua.DuelAutoRefused", who));
        else if (kind == "guild") Info("guilde", Loc.T("L.Lua.SnowGuildAutoRefused"));
        else Info("échange", Loc.Fmt("L.Lua.TradeAutoRefused", who));
    }

    private void OnModeratorPopup(uint pid, LaunchService.ModeratorPopup mp)
    {
        if (pid != _pid || !LoopActive) return;   // hors script : le toast (AccountsViewModel) suffit
        Warn("modération", Loc.Fmt("L.Lua.ModeratorPopup", mp.Author, mp.LockSeconds, mp.Content));
        string? url = _modWebhook;
        if (url is not null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && LicenseService.HasPermission(LicenseService.Perm.PmRelay))
        {
            string name;
            try { name = _world.Get(_pid).CharacterName ?? ""; } catch { name = ""; }
            WebhookService.Post(url, Loc.Fmt("L.Mod.Toast", name.Length > 0 ? name : _pid.ToString(System.Globalization.CultureInfo.InvariantCulture), mp.Content));
        }
        Interlocked.Exchange(ref _modStopPending, 1);
    }

    // ------------------------------------------------------------------ bindings Lua
    private void BindLotE(Script s, CancellationToken ct)
    {
        // ---- combat: verrous, variantes, abandon ----
        var combatV = s.Globals.Get("combat");
        var combat = combatV.Type == DataType.Table ? combatV.Table : new Table(s);
        // lock(spec, join, party) : true = verrouiller cette option (false/nil = ne pas y toucher). Idempotent (bascule côté serveur).
        combat["lock"] = DynValue.NewCallback((_, a) =>
        {
            bool Bo(int i) => ArgV(a, i).CastToBool();
            if (!_launch.IsInFight(_pid)) { Warn("combat", Loc.T("L.Lua.NotInFight")); return DynValue.False; }
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) { Warn("combat", Loc.T("L.Lua.NoPermFight")); return DynValue.False; }
            return DynValue.NewBoolean(_launch.FightLock(_pid, Bo(0), Bo(1), Bo(2)));
        });
        // lockState() -> { spec, join, party } : état CONFIRMÉ par le serveur (FightOptionUpdateEvent) du combat courant.
        combat["lockState"] = DynValue.NewCallback((_, a) =>
        {
            var t = new Table(s);
            t["spec"] = DynValue.NewBoolean(_launch.FightOptionOn(_pid, 0));
            t["join"] = DynValue.NewBoolean(_launch.FightOptionOn(_pid, 2));
            t["party"] = DynValue.NewBoolean(_launch.FightOptionOn(_pid, 1));
            return DynValue.NewTable(t);
        });
        // setSpellVariant(spellId): active la variante (HORS combat), attend la réponse serveur. getSpellVariant = alias tiers
        // (« get » = obtenir). activeVariant(spellId) : lecture du membre actif de la paire (0 si aucun).
        combat["setSpellVariant"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(SetSpellVariant(ArgN(a, 0), ct)));
        combat["getSpellVariant"] = combat.Get("setSpellVariant");
        combat["activeVariant"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(ActiveVariant(ArgN(a, 0))));
        // giveUp() / surrender() : abandonne le combat courant (défaite). true si le combat est effectivement quitté.
        combat["giveUp"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(GiveUp(ct)));
        combat["surrender"] = combat.Get("giveUp");
        s.Globals["combat"] = DynValue.NewTable(combat);
        s.Globals["getSpellVariant"] = combat.Get("setSpellVariant");

        // character:getSpellVariant(id): table get-or-create (ne pas écraser celle du Lot B).
        var chV = s.Globals.Get("character");
        var ch = chV.Type == DataType.Table ? chV.Table : new Table(s);
        ch["getSpellVariant"] = combat.Get("setSpellVariant");
        s.Globals["character"] = DynValue.NewTable(ch);

        // ---- map: point de respawn ----
        var mapV = s.Globals.Get("map");
        var map = mapV.Type == DataType.Table ? mapV.Table : new Table(s);
        map["saveZaap"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(SaveZaap(ct)));
        map["getSavedZaap"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.RespawnMap(_pid)));
        s.Globals["map"] = DynValue.NewTable(map);
        s.Globals["saveZaap"] = map.Get("saveZaap");

        // ---- modération : lastModeratorMessage() -> { author, content, lockDuration, time (unix s), age (s) } ou nil ----
        var lastMod = DynValue.NewCallback((_, a) =>
        {
            if (_launch.LastModeratorPopup(_pid) is not { } m) return DynValue.Nil;
            var t = new Table(s);
            t["author"] = DynValue.NewString(m.Author);
            t["content"] = DynValue.NewString(m.Content);
            t["lockDuration"] = DynValue.NewNumber(m.LockSeconds);
            t["time"] = DynValue.NewNumber(new DateTimeOffset(m.AtUtc, TimeSpan.Zero).ToUnixTimeSeconds());
            t["age"] = DynValue.NewNumber(Math.Round((DateTime.UtcNow - m.AtUtc).TotalSeconds, 1));
            return DynValue.NewTable(t);
        });
        s.Globals["lastModeratorMessage"] = lastMod;
        var gV = s.Globals.Get("global");   // global:lastModeratorMessage: table get-or-create
        var gt = gV.Type == DataType.Table ? gV.Table : new Table(s);
        gt["lastModeratorMessage"] = lastMod;
        s.Globals["global"] = DynValue.NewTable(gt);
    }

    // ------------------------------------------------------------------ verbes (thread du script)
    // map:saveZaap: ouvre le zaap de la carte courante, sauvegarde le respawn, ferme le dialogue. Bloquant.
    private bool SaveZaap(CancellationToken ct)
    {
        if (_launch.IsInFight(_pid)) return false;
        long cur = _launch.CurrentMap(_pid) ?? 0;
        if (cur == 0) return false;
        WaitSelfReady(ct);
        WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == cur, InteractivesDecodeTimeoutMs, ct);   // interactifs décodés en tâche de fond
        if (!_world.Get(_pid).Interactives.Any(i => i.ElementTypeId == 16 && i.HarvestSkill is not null))
        {
            Warn("zaap", Loc.T("L.Lua.SaveZaapNoZaap"));
            return false;
        }
        if (!OpenGroundZaap(cur, ct))
        {
            _launch.LeaveDialog(_pid);
            Warn("zaap", Loc.T("L.Lua.SaveZaapFail"));
            return false;
        }
        if (_launch.RespawnMap(_pid) == cur)   // la liste qui vient d'arriver porte spawn_map_id
        {
            Ok("zaap", Loc.T("L.Lua.SaveZaapAlready"));
            InterruptibleDelay(Random.Shared.Next(300, 700), ct);
            _launch.LeaveDialog(_pid);
            return true;
        }
        long seq0 = _launch.RespawnSeq(_pid);
        if (!_launch.ZaapRespawnSave(_pid))
        {
            _launch.LeaveDialog(_pid);
            Warn("zaap", Loc.T("L.Lua.SaveZaapFail"));
            return false;
        }
        bool ok = WaitUntil(() => _launch.RespawnSeq(_pid) > seq0 || _launch.RespawnMap(_pid) == cur, RespawnSaveTimeoutMs, ct);
        InterruptibleDelay(Random.Shared.Next(300, 700), ct);
        _launch.LeaveDialog(_pid);
        if (ok) Ok("zaap", Loc.Fmt("L.Lua.SaveZaapOk", cur));
        else Warn("zaap", Loc.T("L.Lua.SaveZaapFail"));
        return ok;
    }

    // combat:setSpellVariant(id) : HORS combat ; true si la variante est (déjà) active.
    private bool SetSpellVariant(int spellId, CancellationToken ct)
    {
        if (spellId <= 0) return false;
        if (_launch.IsInFight(_pid)) { Warn("combat", Loc.T("L.Lua.SpellVariantInFight")); return false; }
        if (_launch.LearnedSpells(_pid).Contains(spellId)) return true;   // déjà active
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) { Warn("combat", Loc.T("L.Lua.NoPermFight")); return false; }
        string name = _launch.GameNames.SpellName(spellId) ?? spellId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        long seq0 = _launch.LastSpellVariantResult(_pid).Seq;
        if (!_launch.SpellVariantActivate(_pid, spellId)) { Warn("combat", Loc.Fmt("L.Lua.SpellVariantFail", name)); return false; }
        bool got = WaitUntil(() => { var r = _launch.LastSpellVariantResult(_pid); return r.Seq > seq0 && r.SpellId == spellId; }, SpellVariantTimeoutMs, ct);
        bool ok = got && _launch.LastSpellVariantResult(_pid).Effective;
        if (ok) Ok("combat", Loc.Fmt("L.Lua.SpellVariantOk", name));
        else Warn("combat", Loc.Fmt("L.Lua.SpellVariantFail", name));
        return ok;
    }

    // combat:activeVariant(id) : membre de la paire présent dans les sorts appris, ou 0.
    private int ActiveVariant(int spellId)
    {
        if (spellId <= 0) return 0;
        var learned = _launch.LearnedSpells(_pid);
        var pair = _launch.GameNames.SpellVariantPair(spellId);
        if (pair.Count == 0) return learned.Contains(spellId) ? spellId : 0;
        foreach (var id in pair) if (learned.Contains(id)) return id;
        return 0;
    }

    // combat:giveUp() : ContextQuitRequest puis attente bornée de la sortie de combat. Attente MANUELLE (pas WaitUntil) : dans
    // onTurn, la fin du combat rend le tour « perdu » et WaitUntil déroulerait le hook avant de pouvoir renvoyer true.
    private bool GiveUp(CancellationToken ct)
    {
        if (!_launch.IsInFight(_pid)) { Warn("combat", Loc.T("L.Lua.NotInFight")); return false; }
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) { Warn("combat", Loc.T("L.Lua.NoPermFight")); return false; }
        if (!_launch.FightGiveUp(_pid)) { Warn("combat", Loc.T("L.Lua.GiveUpFailed")); return false; }
        Warn("combat", Loc.T("L.Lua.GiveUpSent"));
        long until = Environment.TickCount64 + GiveUpTimeoutMs;
        while (_launch.IsInFight(_pid) && Environment.TickCount64 < until)
            if (ct.WaitHandle.WaitOne(80)) throw new ScriptStopException();
        bool left = !_launch.IsInFight(_pid);
        if (!left) Warn("combat", Loc.T("L.Lua.GiveUpFailed"));
        return left;
    }
}
