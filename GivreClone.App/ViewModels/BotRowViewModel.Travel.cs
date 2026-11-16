using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

// ===================== Onglet Carte : raccourcis de déplacement (havre-sac, zaap / zaapi de la carte) =====================
// Un clic ouvre le dialogue du jeu ; la fenêtre d'interaction affiche alors la liste des destinations (choix + coût).
// Mêmes chemins que les scripts (marche jusqu'à l'interactif puis utilisation), en MITM comme en Socket.
public partial class BotRowViewModel
{
    private const int ZaapElementType = 16;    // zaap au sol ET zaap du havre-sac (données du jeu)
    private const int ZaapiElementType = 106;
    private const int ZaapiSkill = 157;

    [ObservableProperty] public partial bool HasZaapHere { get; set; }
    [ObservableProperty] public partial bool HasZaapiHere { get; set; }
    [ObservableProperty] public partial bool InHavenBag { get; set; }
    public string HavenBagText => Loc.T(InHavenBag ? "L.Travel.HavenBagLeave" : "L.Travel.HavenBag");
    partial void OnInHavenBagChanged(bool value) => OnPropertyChanged(nameof(HavenBagText));

    private MapInteractive? ZaapHere => MapInteractives?.FirstOrDefault(i => i.ElementTypeId == ZaapElementType && i.HarvestSkill is not null);
    private MapInteractive? ZaapiHere => MapInteractives?.FirstOrDefault(i => i.HarvestSkill is not null
        && (i.ElementTypeId == ZaapiElementType || i.Skills.Any(s => s.SkillId == ZaapiSkill && s.Enabled)));

    /// <summary>Donjon dont la carte courante est une salle (null hors donjon) : bouton « Quitter le donjon ».</summary>
    [ObservableProperty] public partial string? DungeonHere { get; set; }
    public bool InDungeon => DungeonHere is not null;
    public string ExitDungeonTip => DungeonHere is { } n ? Loc.Fmt("L.Travel.ExitDungeonTip", n) : "";
    partial void OnDungeonHereChanged(string? value) { OnPropertyChanged(nameof(InDungeon)); OnPropertyChanged(nameof(ExitDungeonTip)); }
    private long _dungeonCheckedMap = -1;

    /// <summary>Recalcule les raccourcis (appelé à chaque liste d'interactifs et au tick de l'UI).</summary>
    public void RefreshTravelShortcuts()
    {
        HasZaapHere = ZaapHere is not null;
        HasZaapiHere = ZaapiHere is not null;
        InHavenBag = Pid > 0 && Stores.Launch.InHavenBag((uint)Pid);
        if (MapId != _dungeonCheckedMap)   // base du jeu interrogée une fois par carte, pas à chaque tick
        {
            _dungeonCheckedMap = MapId;
            string? name = null;
            try { if (MapId > 0 && Stores.Launch.GameData?.DungeonOfMap(MapId) is { } d) name = string.IsNullOrWhiteSpace(d.Name) ? $"#{d.Id}" : d.Name; }
            catch { name = null; }
            DungeonHere = name;
        }
    }

    /// <summary>Quitte le donjon comme le bouton du jeu (DungeonExitRequest) : retour à l'extérieur. Jamais en combat.</summary>
    [RelayCommand]
    private async Task ExitDungeon()
    {
        if (Pid <= 0 || !InDungeon) return;
        string label = Loc.T("L.Travel.ExitDungeon");
        if (Stores.Launch.IsInFight((uint)Pid)) { Inspector.NoteAction(label, -1, false); return; }
        long from = MapId;
        Inspector.NoteAction(label, -1, true, () => MapId != from);
        var (ok, reason, _) = await Stores.Launch.DungeonExitAsync((uint)Pid, CancellationToken.None);
        if (!ok) Toasts.Show(Loc.Fmt("L.Travel.ExitDungeonFailed", reason), success: false);
    }

    [RelayCommand]
    private void ToggleHavenBag()
    {
        if (Pid <= 0) return;
        long from = MapId;
        bool ok = Stores.Launch.ToggleHavenBag((uint)Pid);
        Inspector.NoteAction(HavenBagText, -1, ok, () => MapId != from);
    }

    [RelayCommand] private void OpenZaapHere() => OpenTeleporter(ZaapHere, "L.Travel.Zaap");
    [RelayCommand] private void OpenZaapiHere() => OpenTeleporter(ZaapiHere, "L.Travel.Zaapi");

    // Marche jusqu'à l'interactif et l'utilise ; confirmé quand la liste des destinations arrive (fenêtre « Téléportation »).
    private void OpenTeleporter(MapInteractive? it, string labelKey)
    {
        if (it is null || Pid <= 0) return;
        bool ok = RequestHarvest(it);
        Inspector.NoteAction(Loc.T(labelKey), it.CellId, ok, () => Stores.Launch.TeleportView((uint)Pid) is not null);
    }
}
