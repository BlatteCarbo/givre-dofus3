using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

/// <summary>Self-contained, offline profession-coaching surface. The shell only mounts this control.</summary>
public partial class ProfessionPlannerPanel : UserControl
{
    public ProfessionPlannerViewModel ViewModel { get; }

    /// <summary>Optional, bounded projection from a verified local profession planner.</summary>
    public Func<ProfessionPlannerGoal, ProfessionPlannerSnapshot?>? PlanProvider
    {
        get => ViewModel.PlanProvider;
        set => ViewModel.PlanProvider = value;
    }

    /// <summary>Optional local recipe source; loading occurs on explicit refresh.</summary>
    public Func<IReadOnlyList<ProfessionCatalogRecipe>>? CatalogProvider
    {
        get => ViewModel.CatalogProvider;
        set => ViewModel.CatalogProvider = value;
    }

    /// <summary>Prime can connect the selected directory to ProfessionCatalogReader.LoadDirectory.</summary>
    public Func<string, IReadOnlyList<ProfessionCatalogRecipe>>? FolderCatalogProvider
    {
        get => ViewModel.FolderCatalogProvider;
        set => ViewModel.FolderCatalogProvider = value;
    }

    /// <summary>Optional atlas link: the caller must certify every hop and its provenance.
    /// The UI keeps UNKNOWN status unless a complete verified route is supplied.</summary>
    public Func<ProfessionPlanStepRow, ProfessionVerifiedAtlasRoute?>? VerifiedAtlasRouteProvider
    {
        get => ViewModel.VerifiedAtlasRouteProvider;
        set => ViewModel.VerifiedAtlasRouteProvider = value;
    }

    /// <summary>Emitted after a real local directory is selected via the native picker.</summary>
    public event Action<string>? CatalogFolderSelected;
    /// <summary>Preferred event name for the prime integration's SelectCatalogDirectory adapter.</summary>
    public event Action<string>? CatalogDirectorySelected;

    public ProfessionPlannerPanel() : this(new ProfessionPlannerViewModel()) { }

    public ProfessionPlannerPanel(ProfessionPlannerViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
    }

    public void LoadCatalog(IEnumerable<ProfessionCatalogRecipe> recipes) => ViewModel.LoadCatalog(recipes);

    public void LoadPlan(ProfessionPlannerSnapshot? snapshot) => ViewModel.LoadPlan(snapshot);

    private async void SelectFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null || !storage.CanPickFolder)
            {
                ViewModel.NotifyPickerUnavailable("Sélection de dossier indisponible sur ce système.");
                return;
            }
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choisir un dossier de catalogue métiers local",
                AllowMultiple = false
            });
            var folder = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folder)) return;
            ViewModel.SetCatalogFolder(folder);
            CatalogFolderSelected?.Invoke(folder);
            CatalogDirectorySelected?.Invoke(folder);
        }
        catch (Exception ex)
        {
            ViewModel.NotifyPickerUnavailable($"Impossible d'ouvrir le dossier : {ex.Message}");
        }
    }
}
