using System;
using System.Threading;
using GivreClone.App.Services.Supervisor;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// Côté BOT du superviseur (MZP23) : la logique vit dans BotSupervisorModule (testée) ; l'hôte lui fournit son arrêt et son journal.
public sealed partial class GivreLuaHost
{
    private BotSupervisorModule? _supervisorModule;

    private void BindSupervisor(Script s, CancellationToken ct)
    {
        _supervisorModule = new BotSupervisorModule((int)_pid, s, ct, SupervisorHub.Bus, SupervisorHub.SnapshotFor,
            ex => Root(ex) is ScriptStopException, () => new ScriptStopException(),
            msg => ScriptErr("hook", new InvalidOperationException(msg), d => d, LuaLogLevel.Warning));
        _supervisorModule.Bind();
    }

    /// <summary>Appelé par PumpHooks : sert onSupervisorMessage (thread du script).</summary>
    private void PumpSupervisorMessages(CancellationToken ct) => _supervisorModule?.Pump();
}
