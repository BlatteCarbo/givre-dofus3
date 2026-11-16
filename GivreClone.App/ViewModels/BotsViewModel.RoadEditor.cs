using CommunityToolkit.Mvvm.ComponentModel;

namespace GivreClone.App.ViewModels;

// ===================== Onglet Trajets : éditeur visuel de trajet =====================
public partial class BotsViewModel
{
    private RoadEditorViewModel? _roadEditor;

    /// <summary>Éditeur visuel de trajet (créé à la 1re utilisation). Le trajet généré va dans l'éditeur Lua du perso sélectionné.</summary>
    public RoadEditorViewModel RoadEditor => _roadEditor ??= new RoadEditorViewModel(
        () => SelectedBot is { Pid: > 0 } b ? (uint)b.Pid : 0u,
        ApplyGeneratedLua,
        () => LuaScript,
        () => { if (SelectedBot is { Pid: > 0 }) RunLua(); },
        () => HdvPlanFor(SelectedBot));

    /// <summary>Lié à TabItem.IsSelected : la mini-carte se prépare à l'ouverture de l'onglet et suit le perso tant qu'il est affiché.</summary>
    [ObservableProperty] public partial bool RoadTabSelected { get; set; }
    partial void OnRoadTabSelectedChanged(bool value) { if (value) RoadEditor.Open(); }

    private void DriveRoadEditor() { if (RoadTabSelected) _roadEditor?.Tick(); }
}
