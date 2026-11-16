using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.NoCode;

namespace GivreClone.App.ViewModels;

/// <summary>Assistant sans code : état de formulaire isolé et prévisualisation uniquement.</summary>
public partial class BotsViewModel
{
    [ObservableProperty] public partial bool NoCodeWizardOpen { get; set; }
    [ObservableProperty] public partial string NoCodeMaps { get; set; } =
        "4,-22:right\n5,-22:bottom\n5,-21:left\n4,-21:top";
    [ObservableProperty] public partial bool NoCodeGather { get; set; }
    [ObservableProperty] public partial bool NoCodeFight { get; set; }
    [ObservableProperty] public partial string NoCodeGatherIds { get; set; } = "38";
    [ObservableProperty] public partial string NoCodeMaxPods { get; set; } = "90";
    [ObservableProperty] public partial string NoCodeMinMonsters { get; set; } = "1";
    [ObservableProperty] public partial string NoCodeMaxMonsters { get; set; } = "8";
    [ObservableProperty] public partial string NoCodeGeneratedLua { get; set; } = "";
    [ObservableProperty] public partial string NoCodeMessage { get; set; } =
        "Configurez le trajet, puis cliquez sur Prévisualiser.";
    [ObservableProperty] public partial bool NoCodePreviewReady { get; set; }
    [ObservableProperty] public partial bool NoCodeReplaceConfirm { get; set; }

    private string? _noCodeSourceSnapshot;
    private BotRowViewModel? _noCodeBotSnapshot;

    partial void OnNoCodeMapsChanged(string value) => InvalidateNoCode();
    partial void OnNoCodeGatherChanged(bool value) => InvalidateNoCode();
    partial void OnNoCodeFightChanged(bool value) => InvalidateNoCode();
    partial void OnNoCodeGatherIdsChanged(string value) => InvalidateNoCode();
    partial void OnNoCodeMaxPodsChanged(string value) => InvalidateNoCode();
    partial void OnNoCodeMinMonstersChanged(string value) => InvalidateNoCode();
    partial void OnNoCodeMaxMonstersChanged(string value) => InvalidateNoCode();

    private void InvalidateNoCode()
    {
        NoCodePreviewReady = false;
        NoCodeReplaceConfirm = false;
        NoCodeGeneratedLua = "";
        _noCodeSourceSnapshot = null;
        _noCodeBotSnapshot = null;
        NoCodeMessage = "Configuration modifiée : relancez la prévisualisation.";
    }

    [RelayCommand]
    private void NoCodeOpen()
    {
        EnsureNoCodeLot2();
        InvalidateNoCode();
        NoCodeWizardOpen = true;
    }

    [RelayCommand]
    private void NoCodeClose()
    {
        NoCodeWizardOpen = false;
        NoCodeReplaceConfirm = false;
    }

    [RelayCommand]
    private void NoCodePresetRoute()
    {
        NoCodeGather = false;
        NoCodeFight = false;
        InvalidateNoCode();
    }

    [RelayCommand]
    private void NoCodePresetGather()
    {
        NoCodeGather = true;
        NoCodeFight = false;
        InvalidateNoCode();
    }

    [RelayCommand]
    private void NoCodePresetFight()
    {
        NoCodeGather = false;
        NoCodeFight = true;
        InvalidateNoCode();
    }

    [RelayCommand]
    private void NoCodePreview()
    {
        NoCodeReplaceConfirm = false;
        var result = NoCodeLuaGenerator.Generate(new NoCodeRouteOptions(
            NoCodeMaps, NoCodeGather, NoCodeFight,
            NoCodeGatherIds, NoCodeMaxPods, NoCodeMinMonsters, NoCodeMaxMonsters));
        NoCodeMessage = result.Message;
        NoCodeGeneratedLua = result.Lua;
        NoCodePreviewReady = result.IsValid;
        _noCodeSourceSnapshot = result.IsValid ? LuaScript : null;
        _noCodeBotSnapshot = result.IsValid ? SelectedBot : null;
    }

    /// <summary>La confirmation explicite avertit même si le fichier n'est pas marqué modifié :
    /// les scripts de personnages en mémoire peuvent contenir des modifications non exportées.</summary>
    [RelayCommand]
    private void NoCodeRequestReplace()
    {
        if (!NoCodeCanReplace())
        {
            InvalidateNoCode();
            NoCodeMessage = "Le script ou le personnage a changé. Reprévisualisez avant d'importer.";
            return;
        }
        NoCodeReplaceConfirm = true;
    }

    [RelayCommand]
    private void NoCodeCancelReplace() => NoCodeReplaceConfirm = false;

    [RelayCommand]
    private void NoCodeConfirmReplace()
    {
        if (!NoCodeReplaceConfirm || !NoCodeCanReplace())
        {
            InvalidateNoCode();
            NoCodeMessage = "Import annulé : contenu ou personnage modifié depuis la prévisualisation.";
            return;
        }
        // Opération réversible via l'historique Undo ; le fichier précédemment ouvert est détaché.
        if (!ApplyGeneratedLua(NoCodeGeneratedLua))
        {
            NoCodeReplaceConfirm = false;
            NoCodeMessage = "L'éditeur est verrouillé pour ce personnage.";
            return;
        }
        NoCodeWizardOpen = false;
        InvalidateNoCode();
    }

    private bool NoCodeCanReplace() =>
        NoCodePreviewReady
        && !string.IsNullOrEmpty(NoCodeGeneratedLua)
        && SelectedBot?.FollowerLocked != true
        && ReferenceEquals(SelectedBot, _noCodeBotSnapshot)
        && string.Equals(LuaScript, _noCodeSourceSnapshot, StringComparison.Ordinal);
}
