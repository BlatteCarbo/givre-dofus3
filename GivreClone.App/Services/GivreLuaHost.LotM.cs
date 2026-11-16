using System.Threading;
using GivreClone.App.Services.LotM;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// MZP23 lot M : modules Lua achievement/anomaly/teleport/dungeon/document/data/job.recipes/quest/character/bank/sale/npc (logique dans LotM/LotMLuaModule).
public sealed partial class GivreLuaHost
{
    private void BindLotM(Script s, CancellationToken ct)
        => new LotMLuaModule(s, new LotMGame(_launch, _pid, _world), ct, name => GetOrCreateTable(s, name),
            () => new ScriptStopException(),
            msg => ScriptErr("hook", new System.InvalidOperationException(msg), d => d, LuaLogLevel.Warning)).Bind();
}
