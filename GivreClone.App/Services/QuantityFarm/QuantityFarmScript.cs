using System.Globalization;
using System.Text;
using GivreClone.Protocol;

namespace GivreClone.App.Services.QuantityFarm;

public enum QuantityFarmKind { Resource, Monster }

public enum QuantityFarmStep { Gather, Fight }

public sealed record QuantityFarmScriptSpec(
    QuantityFarmKind Kind,
    long ItemGid,
    long Target,
    int MonsterId,
    int MinMonsters,
    int MaxMonsters,
    int MaxPods,
    string DisplayName,
    IReadOnlyList<long> MapIds);

/// <summary>Texte Lua seulement. Un pas de récolte ou un combat par move(), puis le compteur est relu.</summary>
public static class QuantityFarmScript
{
    public static string Build(QuantityFarmScriptSpec spec)
    {
        if (spec.MapIds is null || spec.MapIds.Count == 0 || spec.MapIds.Any(id => id <= 0))
            throw new ArgumentException("circuit vide");
        if (spec.ItemGid <= 0 || spec.Target < 1 || spec.Target > 1_000_000_000L)
            throw new ArgumentException("Invalid resource demand.");
        int pods = spec.MaxPods < 1 ? 95 : Math.Clamp(spec.MaxPods, 1, 100);
        var sb = new StringBuilder();
        sb.Append("-- Farm par quantité : ").Append(Clean(spec.DisplayName)).Append(" (").Append(I(spec.ItemGid))
            .Append("), objectif ").Append(I(spec.Target)).Append(".\n");
        sb.Append("-- Le compteur est celui du sac. Pas de dépôt : déposer ferait retomber le compte.\n");
        if (spec.Kind == QuantityFarmKind.Resource)
        {
            sb.Append("ITEMS_TO_GATHER = { ").Append(I(spec.ItemGid)).Append(" }\n");
        }
        else
        {
            if (spec.MonsterId <= 0) throw new ArgumentException("monstre requis");
            int min = Math.Clamp(spec.MinMonsters, 1, 8);
            int max = Math.Clamp(spec.MaxMonsters, 1, 8);
            if (min > max) (min, max) = (max, min);
            sb.Append("FORCE_MONSTERS = { ").Append(spec.MonsterId.ToString(CultureInfo.InvariantCulture)).Append(" }\n");
            sb.Append("MIN_MONSTERS = ").Append(min.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("MAX_MONSTERS = ").Append(max.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        sb.Append("MAX_PODS = ").Append(pods.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("local GID = ").Append(I(spec.ItemGid)).Append('\n');
        sb.Append("local TARGET = ").Append(I(spec.Target)).Append("\n\n");
        sb.Append("function move()\n");
        sb.Append("  local owned = getInventoryItemCount(GID)\n");
        sb.Append("  local remaining = TARGET - owned\n");
        sb.Append("  if remaining < 0 then remaining = 0 end\n");
        sb.Append("  printMessage(\"[quantity-farm] gid=\" .. GID .. \" owned=\" .. owned .. \" target=\" .. TARGET .. \" remaining=\" .. remaining)\n");
        sb.Append("  if owned >= TARGET then return false end\n");
        sb.Append("  if getPodsPercent() >= MAX_PODS then return false end\n");
        AppendRoute(sb, spec.MapIds, spec.Kind == QuantityFarmKind.Resource ? "gather" : "fight");
        sb.Append("end\n");
        return sb.ToString();
    }

    private static void AppendRoute(StringBuilder sb, IReadOnlyList<long> maps, string action)
    {
        sb.Append("  return {\n");
        for (int i = 0; i < maps.Count; i++)
        {
            sb.Append("    { map = \"").Append(I(maps[i])).Append("\", ").Append(action).Append(" = true");
            if (maps.Count > 1)
                sb.Append(", changeMap = \"").Append(I(maps[(i + 1) % maps.Count])).Append('"');
            sb.Append(" },\n");
        }
        sb.Append("  }\n");
    }

    private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "quantité";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c < ' ') { sb.Append(' '); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Même règle d'arrêt que le script, sans hôte : une action par move(), le butin s'ajoute après.</summary>
public sealed class QuantityFarmSession
{
    public const string HarvestAction = "InteractiveUseRequest";
    public const string AttackAction = "AttackMonsterRequest";
    private readonly List<string> _trace = new();

    public QuantityFarmSession(long gid, long owned, long target)
    {
        if (gid <= 0 || target < 1 || target > 1_000_000_000L || owned < 0)
            throw new ArgumentException("Invalid resource demand.");
        Gid = gid;
        Owned = owned;
        Target = target;
    }

    public long Gid { get; }
    public long Owned { get; private set; }
    public long Target { get; }
    public IReadOnlyList<string> Trace => _trace;
    public string Line => QuantityFarmProgress.FormatLine(Gid, Owned, Target);

    public bool Move(QuantityFarmStep step)
    {
        if (Owned >= Target) return false;
        _trace.Add(step == QuantityFarmStep.Gather ? HarvestAction : AttackAction);
        return true;
    }

    public void ApplyHarvest(long quantity)
    {
        if (quantity > 0) Owned += quantity;
    }

    public void ApplyLoot(FightEndSummary summary)
    {
        if (!summary.Found || summary.Loot is null) return;
        foreach (var (gid, qty) in summary.Loot)
            if (gid == Gid && qty > 0) Owned += qty;
    }
}
