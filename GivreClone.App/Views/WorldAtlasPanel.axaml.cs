using System;
using Avalonia.Controls;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

/// <summary>Standalone atlas UI. The shell only mounts this UserControl; a verified local
/// snapshot can be injected through ViewModel.LoadSnapshot / ViewModel.SnapshotProvider.</summary>
public partial class WorldAtlasPanel : UserControl
{
    public WorldAtlasViewModel ViewModel { get; }
    /// <summary>Optional local verified-snapshot adapter for prime. The refresh button calls it.</summary>
    public Func<WorldAtlasSnapshot?>? SnapshotProvider
    {
        get => ViewModel.SnapshotProvider;
        set => ViewModel.SnapshotProvider = value;
    }

    public WorldAtlasPanel() : this(new WorldAtlasViewModel()) { }

    public WorldAtlasPanel(WorldAtlasViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
    }

    /// <summary>Allows a local atlas snapshot to be pushed after importer verification.</summary>
    public bool LoadSnapshot(WorldAtlasSnapshot? snapshot) => ViewModel.LoadSnapshot(snapshot);
}
