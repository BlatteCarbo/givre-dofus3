using System;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// ===================== Onglet Infos / carte État : compte et progression =====================
// Abonnement, kamas (sac et coffre), expérience du niveau, énergie, zone courante. Tout est lu des paquets déjà reçus
// (aucune requête au serveur) ; rafraîchi au tick lent de l'UI pour le bot affiché seulement.
public partial class BotRowViewModel
{
    [ObservableProperty] public partial string ZoneText { get; set; } = "—";
    /// <summary>Rendement des dernières 24 h (« 12 300 xp/h · 4 100 k/h »), vide tant que rien n'est mesuré. Rempli par la Vue d'ensemble.</summary>
    [ObservableProperty] public partial string DayRatesText { get; set; } = "";
    [ObservableProperty] public partial string SubscriptionText { get; set; } = "—";
    [ObservableProperty] public partial bool SubscriptionWarn { get; set; }
    [ObservableProperty] public partial string InfoKamasText { get; set; } = "—";
    [ObservableProperty] public partial string InfoBankKamasText { get; set; } = "—";
    /// <summary>Infobulle de la tuile Kamas : kamas en banque (session ou dernier relevé daté) et total sac + banque.</summary>
    [ObservableProperty] public partial string KamasTooltip { get; set; } = "";
    [ObservableProperty] public partial string InfoXpText { get; set; } = "—";
    [ObservableProperty] public partial string XpLevelText { get; set; } = "";
    [ObservableProperty] public partial double XpPercent { get; set; }
    [ObservableProperty] public partial bool HasXpBar { get; set; }
    [ObservableProperty] public partial string EnergyText { get; set; } = "—";
    [ObservableProperty] public partial double EnergyPercent { get; set; }
    [ObservableProperty] public partial bool HasEnergy { get; set; }
    /// <summary>Altérations actives (élixirs uchroniques nommés, autres par id) ; vide si aucune connue.</summary>
    [ObservableProperty] public partial string AlterationsText { get; set; } = "";
    public bool HasAlterations => AlterationsText.Length > 0;
    partial void OnAlterationsTextChanged(string value) => OnPropertyChanged(nameof(HasAlterations));

    private static string N0(long v) => v.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Nouvelle session : rien de l'ancienne n'est affiché.</summary>
    private void ResetInfos()
    {
        ZoneText = SubscriptionText = InfoKamasText = InfoBankKamasText = InfoXpText = EnergyText = "—";
        XpLevelText = AlterationsText = ""; SubscriptionWarn = HasXpBar = HasEnergy = false; XpPercent = EnergyPercent = 0;
    }

    /// <summary>Relit les valeurs du compte et du perso (tick lent, bot affiché).</summary>
    public void RefreshInfos()
    {
        var l = Stores.Launch;
        uint pid = (uint)Math.Max(0, Pid);
        if (pid == 0) return;

        int sub = l.CurrentSubArea(pid);
        if (sub > 0)
        {
            string? subName = l.GameNames.SubAreaName(sub);
            int area = l.GameNames.AreaOfSubArea(sub);
            string? areaName = area >= 0 ? l.GameNames.AreaName(area) : null;
            ZoneText = areaName is null ? subName ?? $"#{sub}"
                     : subName is null || subName == areaName ? areaName : $"{areaName} ({subName})";
        }
        else ZoneText = "—";

        if (l.SubscriptionEndUtc(pid) is { } end)
        {
            var left = end - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) { SubscriptionText = Loc.T("L.Info.SubNone"); SubscriptionWarn = true; }
            else
            {
                string date = end.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
                SubscriptionText = left.TotalDays >= 1 ? Loc.Fmt("L.Info.SubDays", date, (int)left.TotalDays)
                                                       : Loc.Fmt("L.Info.SubHours", date, Math.Max(1, (int)left.TotalHours));
                SubscriptionWarn = left.TotalDays < 3;
            }
        }
        else { SubscriptionText = "—"; SubscriptionWarn = false; }

        long k = l.Kamas(pid);
        InfoKamasText = k >= 0 ? N0(k) : "—";
        long bk = l.BankKamas(pid);
        var cfg = BotSettingsStore.Get(Row.CharacterName);
        if (bk >= 0 && bk != cfg.LastBankKamas && !string.IsNullOrWhiteSpace(Row.CharacterName))
        { cfg.LastBankKamas = bk; cfg.LastBankKamasUtc = DateTime.UtcNow; BotSettingsStore.Save(); }   // relevé gardé d'une session à l'autre
        long shown = bk >= 0 ? bk : cfg.LastBankKamas;
        string? seen = bk < 0 && cfg.LastBankKamasUtc is { } at ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : null;
        InfoBankKamasText = shown < 0 ? Loc.T("L.Info.BankUnknown") : seen is null ? N0(shown) : Loc.Fmt("L.Info.BankSeen", N0(shown), seen);
        KamasTooltip = shown < 0
            ? $"{Loc.T("L.Info.BankKamas")} : {Loc.T("L.Info.BankUnknown")}"
            : $"{Loc.T("L.Info.BankKamas")} : {InfoBankKamasText}" + (k >= 0 ? Environment.NewLine + $"{Loc.T("L.Info.KamasTotal")} : {N0(k + shown)}" : "");

        long xp = l.CharacterXp(pid), floor = l.CharacterXpLevelFloor(pid), next = l.CharacterXpNextLevel(pid);
        XpLevelText = Row.Level is { } lv ? Loc.Fmt("L.Info.LevelN", lv) : "";
        if (xp >= 0 && floor >= 0 && next > floor && xp >= floor)
        {
            double pct = Math.Clamp(100.0 * (xp - floor) / (next - floor), 0, 100);
            XpPercent = pct;
            HasXpBar = true;
            InfoXpText = $"{N0(xp - floor)} / {N0(next - floor)}  ({pct.ToString("0.0", CultureInfo.CurrentCulture)} %)";
        }
        else
        {
            HasXpBar = false;
            InfoXpText = xp >= 0 ? N0(xp) : "—";
        }

        if (l.SelfEnergy(pid) is { } en && en.Max > 0)
        {
            HasEnergy = true;
            EnergyPercent = Math.Clamp(100.0 * en.Cur / en.Max, 0, 100);
            EnergyText = $"{N0(en.Cur)} / {N0(en.Max)}";
        }
        else { HasEnergy = false; EnergyText = "—"; }

        AlterationsText = string.Join(" · ", l.Alterations(pid).Select(a =>
        {
            var tier = UchronicElixirs.ByAlteration(a.Id);
            string name = tier is not null ? l.ItemDisplayName(tier.Gid) : Loc.Fmt("L.Info.AlterationN", a.Id);
            return tier is not null && UchronicElixirs.IsFightCount(tier, a.ExpirationValue) ? Loc.Fmt("L.Info.AlterationFights", name, a.ExpirationValue) : name;
        }));
    }
}
