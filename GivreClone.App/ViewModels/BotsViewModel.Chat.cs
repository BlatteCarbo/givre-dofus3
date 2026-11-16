using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Chat;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de la console de discussion (MP, canaux) : heure, canal, expéditeur, texte.</summary>
public sealed record ChatLineVm(string Time, string Label, string Sender, string Text, bool Outgoing, bool IsPrivate)
{
    public int Channel { get; init; }
    private static readonly IBrush Mp = new ImmutableSolidColorBrush(Color.Parse("#F2C14E"));
    private static readonly IBrush Out = new ImmutableSolidColorBrush(Color.Parse("#8BD5A0"));
    private static readonly IBrush Normal = new ImmutableSolidColorBrush(Color.Parse("#C9D1DC"));
    public IBrush Brush => new ImmutableSolidColorBrush(Color.Parse(Channel switch
    {
        1 or 4 => "#7FB2FF", 2 => "#B39DDB", 3 => "#CDB77B", 5 => "#D8AE77",
        6 => "#D4D780", 10 => "#F2C14E", 11 => "#EAA4CF", -1 or 9 => "#8BD5A0", _ => "#C9D1DC"
    }));
    public string SenderText => (Outgoing ? "→ " : "") + Sender;
    public string ReadableText => string.Concat(ChatMarkup.Parse(Text).Select(p => p.Kind switch
    {
        "chatitem" => Stores.Launch.ItemDisplayName(p.Id),
        "chatmonster" => Stores.Launch.GameNames.MonsterName(p.Id) ?? $"Monstre #{p.Id}",
        "chatachievement" => Stores.Launch.GameData?.Achievement(p.Id)?.Name ?? $"Succès #{p.Id}",
        _ => p.Text
    }));
    public string Head => $"{Time} [{Label}] " + (Outgoing ? "→ " : "") + (Sender.Length > 0 ? Sender + " : " : "");
    public static ChatLineVm From(ChatEntry e) => new(e.At.ToString("HH:mm:ss"), e.Label, e.Sender, e.Content, e.Outgoing, e.Channel == ChatBuffer.PrivateChannel) { Channel = e.Channel };
}

// Console de discussion du bot affiché (tous canaux, MP envoyés) : deuxième console à côté de la console technique. Lecture seule sauf l'envoi d'un MP.
public partial class BotsViewModel
{
    private const int ChatKeep = 400;
    public ObservableCollection<ChatLineVm> ChatLines { get; } = new();

    [ObservableProperty] private bool _chatOnlyPrivate;

    private void InitChat()
    {
        Stores.Launch.ChatReceived += (pid, e) => Dispatcher.UIThread.Post(() =>
        {
            if (SelectedBot is not { Pid: > 0 } b || (uint)b.Pid != pid) return;
            if (ChatOnlyPrivate && e.Channel != ChatBuffer.PrivateChannel) return;
            ChatLines.Add(ChatLineVm.From(e));
            while (ChatLines.Count > ChatKeep) ChatLines.RemoveAt(0);
        });
        PropertyChanged += (_, a) => { if (a.PropertyName == nameof(SelectedBot)) { ReloadChat(); FightDiagnosticText = ""; } };
    }

    partial void OnChatOnlyPrivateChanged(bool value) => ReloadChat();

    [RelayCommand]
    private void ClearChat()
    {
        if (SelectedBot is { Pid: > 0 } bot) Stores.Launch.ChatOf((uint)bot.Pid).Clear();
        ChatLines.Clear();
    }

    private void ReloadChat()
    {
        ChatLines.Clear();
        if (SelectedBot is not { Pid: > 0 } b) return;
        foreach (var e in Stores.Launch.ChatOf((uint)b.Pid).Since(0, ChatKeep, ChatOnlyPrivate ? "mp" : "all")) ChatLines.Add(ChatLineVm.From(e));
    }
}
