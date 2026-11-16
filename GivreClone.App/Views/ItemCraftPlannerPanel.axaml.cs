using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

/// <summary>Standalone, entirely offline item/set composer. The prime hosts it without shell changes here.</summary>
public partial class ItemCraftPlannerPanel : UserControl
{
    public ItemCraftPlannerViewModel ViewModel { get; }

    public Func<IReadOnlyList<ItemCraftCatalogEntry>>? CatalogProvider
    {
        get => ViewModel.CatalogProvider;
        set => ViewModel.CatalogProvider = value;
    }

    public Func<string, IReadOnlyList<ItemCraftCatalogEntry>>? FolderCatalogProvider
    {
        get => ViewModel.FolderCatalogProvider;
        set => ViewModel.FolderCatalogProvider = value;
    }

    public Func<ItemCraftPlannerRequest, ItemCraftPlanSnapshot?>? PlanProvider
    {
        get => ViewModel.PlanProvider;
        set => ViewModel.PlanProvider = value;
    }

    public event Action<string>? CatalogDirectorySelected;

    public ItemCraftPlannerPanel() : this(new ItemCraftPlannerViewModel()) { }

    public ItemCraftPlannerPanel(ItemCraftPlannerViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
    }

    public void LoadCatalog(IEnumerable<ItemCraftCatalogEntry> entries) => ViewModel.LoadCatalog(entries);
    public void LoadPlan(ItemCraftPlanSnapshot? snapshot) => ViewModel.LoadPlan(snapshot);

    private async void ChooseCatalogFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null || !provider.CanPickFolder)
            {
                ViewModel.NotifyPickerError("Le sélecteur de dossier local est indisponible.");
                return;
            }
            var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choisir un dossier de catalogue d'objets et de panoplies",
                AllowMultiple = false
            });
            var folder = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folder)) return;
            ViewModel.SetCatalogFolder(folder);
            CatalogDirectorySelected?.Invoke(folder);
        }
        catch (Exception ex)
        {
            ViewModel.NotifyPickerError($"Sélection impossible : {ex.Message}");
        }
    }
}
