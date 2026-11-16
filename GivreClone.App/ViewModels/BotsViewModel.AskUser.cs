using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// ===================== Question d'un script (askInput / askQuestion) =====================
// Le script attend la réponse sur son propre thread ; la fenêtre se ferme seule si le script s'arrête ou après 5 min.
// Une nouvelle question remplace la précédente (réponse vide / non).
public partial class BotsViewModel
{
    private const int AskUserTimeoutMs = 5 * 60_000;
    private TaskCompletionSource<string?>? _askTcs;

    [ObservableProperty] public partial bool AskVisible { get; set; }
    [ObservableProperty] public partial bool AskIsQuestion { get; set; }
    [ObservableProperty] public partial string AskTitle { get; set; } = "";
    [ObservableProperty] public partial string AskPrompt { get; set; } = "";
    [ObservableProperty] public partial string AskAnswer { get; set; } = "";
    public bool AskIsInput => !AskIsQuestion;
    partial void OnAskIsQuestionChanged(bool value) => OnPropertyChanged(nameof(AskIsInput));

    /// <summary>Pose la question d'un script : texte saisi (askInput) ou « oui » / null (askQuestion) ; null = annulé, délai ou arrêt.</summary>
    public async Task<string?> AskUserAsync(int pid, string prompt, string defaultAnswer, bool question, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _askTcs?.TrySetResult(null);
            string who = _byPid.TryGetValue(pid, out var bot) ? bot.Row.CharacterName ?? $"pid {pid}" : $"pid {pid}";
            AskTitle = Loc.Fmt("L.Lua.AskTitle", who);
            AskPrompt = string.IsNullOrWhiteSpace(prompt) ? Loc.T(question ? "L.Lua.AskDefaultQuestion" : "L.Lua.AskDefaultInput") : prompt;
            AskAnswer = defaultAnswer ?? "";
            AskIsQuestion = question;
            _askTcs = tcs; AskVisible = true;
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AskUserTimeoutMs);
        try
        {
            await using (timeout.Token.Register(() => tcs.TrySetResult(null)))
                return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (ReferenceEquals(_askTcs, tcs)) { _askTcs = null; AskVisible = false; } });
        }
    }

    [RelayCommand] private void AskConfirm() { AskVisible = false; _askTcs?.TrySetResult(AskIsQuestion ? "yes" : AskAnswer ?? ""); }
    [RelayCommand] private void AskCancel() { AskVisible = false; _askTcs?.TrySetResult(null); }
}
