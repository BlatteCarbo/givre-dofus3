using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.Social;
using GivreCore.Protobuf;

namespace GivreClone.App.ViewModels;

public partial class ChannelFilter : ObservableObject
{
    private readonly Action _changed;
    public int Id { get; }
    public string Label { get; }
    [ObservableProperty] public partial bool Shown { get; set; }

    public ChannelFilter(int id, bool shown, Action changed)
    {
        Id = id;
        Label = ChannelPlan.Label(id);
        _changed = changed;
        Shown = shown;
    }

    partial void OnShownChanged(bool value) => _changed();
}

public sealed class SocialLinkVm
{
    public string Label { get; }
    public ICommand Open { get; }
    public ItemHoverModel? Item { get; }
    public MonsterHoverModel? Monster { get; }
    public bool HasItem => Item is not null;
    public bool HasMonster => Monster is not null;
    public bool HasCard => HasItem || HasMonster;

    public SocialLinkVm(string label, Action open, ItemHoverModel? item, MonsterHoverModel? monster)
    {
        Label = label;
        Open = new RelayCommand(open);
        Item = item;
        Monster = monster;
    }
}

public sealed class SocialChatLine
{
    public int Channel { get; }
    public string Head { get; }
    public string Text { get; }
    public string SenderLabel { get; }
    public bool HasSender { get; }
    public ICommand? OpenSender { get; }
    public IReadOnlyList<SocialLinkVm> Links { get; }

    public SocialChatLine(int channel, string head, string text, string senderLabel, bool hasSender, ICommand? openSender, IReadOnlyList<SocialLinkVm> links)
    {
        Channel = channel;
        Head = head;
        Text = text;
        SenderLabel = senderLabel;
        HasSender = hasSender;
        OpenSender = openSender;
        Links = links;
    }
}

/// <summary>
/// Panneau profil et discussion. Le parent branche, sans modifier les fichiers déjà pris par d'autres agents :
/// 1. BotsViewModel expose Social. Au changement de bot : Social.Clear().
/// 2. BotsView.axaml : TabItem dont le contenu est SocialView, DataContext = Social.
///    Le ViewLocator résout aussi SocialViewModel vers SocialView.
/// 3. Paquets, hors LaunchService.Packets.cs : CardInfoResponse → IngestCardInfo(payload) ;
///    CardViewResponse → IngestCardView ; ChatChannelMessageEvent → IngestChatEvent ;
///    ChannelUpdateEvent → IngestChannelUpdate. Le SubscribeMultipleChannelRequest vu en sortie
///    appelle NoteOutgoingSubscribe et n'est pas renvoyé.
/// 4. Social.Inject = frame =&gt; relay.Inject(pid, frame) avec la trame déjà préfixée par sa longueur.
///    Social.IsMitm selon le mode. Social.CodeForName / FieldNumber depuis ProtocolCatalog.
///    Social.Names = gameDb.SocialNames(). Social.AveragePrice depuis les prix déjà connus.
///    Social.EffectTemplate si un gabarit d'effet est déjà en mémoire.
/// 5. Clic sur un joueur de la carte : RememberPlayer(nom, id) puis RequestCharacter(id, nom).
/// 6. Ailleurs dans l'app, ItemCard a pour DataContext un ItemHoverModel (HoverCards.Item).
///    ItemTooltip.cs reste inchangé.
/// 7. Historique déjà en mémoire : AddObservedChat (texte seul, sans effets d'instance).
/// L'abonnement à tous les canaux part seulement du bouton, et en MITM seulement si
/// AllowGameChannelCheckboxes est coché. Les cases du panneau ne font que filtrer l'affichage.
/// </summary>
public partial class SocialViewModel : ViewModelBase
{
    private readonly object _gate = new();
    private readonly PlayerDirectory _directory = new();
    private readonly ProfileMemory _memory = new();
    private readonly List<SocialChatLine> _all = new();
    private int _generation;

    public SocialViewModel()
    {
        foreach (int id in ChannelPlan.AllChannels)
            Channels.Add(new ChannelFilter(id, true, RefreshShown));
    }

    public ObservableCollection<ChannelFilter> Channels { get; } = new();
    public ObservableCollection<SocialChatLine> Messages { get; } = new();
    public ObservableCollection<ProfileRow> Rows { get; } = new();
    public ObservableCollection<EquipmentRow> Equipment { get; } = new();

    public Func<byte[], bool>? Inject { get; set; }
    public Func<string, string?> CodeForName { get; set; } = static _ => null;
    public Func<string, string, int?> FieldNumber { get; set; } = static (_, _) => null;
    public ISocialNames Names { get; set; } = EmptySocialNames.Instance;
    public Func<long, long?>? AveragePrice { get; set; }
    public Func<int, string?>? EffectTemplate { get; set; }
    public Func<int>? AllocateUid { get; set; }
    public int RequestUid { get; set; } = -1;

    public byte[] LastCardInfoFrame { get; private set; } = Array.Empty<byte>();
    public byte[] LastCardViewFrame { get; private set; } = Array.Empty<byte>();
    public byte[] LastEquipmentFrame { get; private set; } = Array.Empty<byte>();
    public byte[] LastSubscribeFrame { get; private set; } = Array.Empty<byte>();

    [ObservableProperty] public partial bool IsMitm { get; set; } = true;
    [ObservableProperty] public partial bool AllowGameChannelCheckboxes { get; set; }
    [ObservableProperty] public partial string ProfileName { get; set; } = "";
    [ObservableProperty] public partial string ShownName { get; set; } = "";
    [ObservableProperty] public partial string ProfileStatus { get; set; } = "Le profil se demande avec l'identifiant du personnage.";
    [ObservableProperty] public partial string ChannelStatus { get; set; } = ChannelPlan.BoxesStay;
    [ObservableProperty] public partial string ServerChannels { get; set; } = "Abonnement du jeu pas encore observé.";
    [ObservableProperty] public partial string LastChannelChange { get; set; } = "";
    [ObservableProperty] public partial string EquipmentNote { get; set; } = ProfileText.IdleTab;
    [ObservableProperty] public partial bool HasEquipmentNote { get; set; } = true;
    [ObservableProperty] public partial ItemHoverModel? SelectedItem { get; set; }
    [ObservableProperty] public partial MonsterHoverModel? SelectedMonster { get; set; }

    public bool HasSelectedItem => SelectedItem is not null;
    public bool HasSelectedMonster => SelectedMonster is not null;

    partial void OnSelectedItemChanged(ItemHoverModel? value) => OnPropertyChanged(nameof(HasSelectedItem));
    partial void OnSelectedMonsterChanged(MonsterHoverModel? value) => OnPropertyChanged(nameof(HasSelectedMonster));
    partial void OnIsMitmChanged(bool value) => SubscribeAllCommand.NotifyCanExecuteChanged();

    partial void OnAllowGameChannelCheckboxesChanged(bool value)
    {
        SubscribeAllCommand.NotifyCanExecuteChanged();
        if (IsMitm && value) ChannelStatus = ChannelPlan.BoxesWillChangeOnAsk;
        else if (IsMitm) ChannelStatus = ChannelPlan.BoxesStay;
    }

    private bool CanSubscribeAll() => ChannelPlan.MaySend(IsMitm, AllowGameChannelCheckboxes);

    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _directory.Clear();
            _memory.Begin(false);
        }
        _all.Clear();
        Run(() =>
        {
            Messages.Clear();
            Rows.Clear();
            Equipment.Clear();
            ShownName = "";
            ProfileStatus = "Le profil se demande avec l'identifiant du personnage.";
            EquipmentNote = ProfileText.IdleTab;
            HasEquipmentNote = true;
            ServerChannels = "Abonnement du jeu pas encore observé.";
            LastChannelChange = "";
            SelectedItem = null;
            SelectedMonster = null;
            LastCardInfoFrame = Array.Empty<byte>();
            LastCardViewFrame = Array.Empty<byte>();
            LastEquipmentFrame = Array.Empty<byte>();
            LastSubscribeFrame = Array.Empty<byte>();
        });
    }

    public void RememberPlayer(string name, long id)
    {
        lock (_gate) _directory.Remember(name, id);
    }

    [RelayCommand]
    private void RequestProfile()
    {
        string name = (ProfileName ?? "").Trim();
        if (name.Length == 0)
        {
            ProfileStatus = "Indiquez un nom, ou choisissez un joueur déjà vu.";
            return;
        }
        bool known;
        long id;
        lock (_gate) known = _directory.TryGet(name, out id);
        if (!known)
        {
            ProfileStatus = ProfileText.NameNotSeen;
            return;
        }
        RequestCharacter(id, name);
    }

    public void RequestCharacter(long id, string? name)
    {
        if (id <= 0) return;
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(name)) _directory.Remember(name, id);
            _memory.Begin(true);
        }
        int infoUid = AllocateUid?.Invoke() ?? RequestUid;
        int viewUid = AllocateUid?.Invoke() ?? RequestUid;
        int equipmentUid = AllocateUid?.Invoke() ?? RequestUid;
        byte[] info = SocialFrames.CardInfoMessage(CodeForName, FieldNumber, id, infoUid);
        byte[] view = SocialFrames.CardViewMessage(CodeForName, FieldNumber, id, 0, viewUid);
        byte[] equipment = SocialFrames.CardViewMessage(CodeForName, FieldNumber, id, 1, equipmentUid);
        LastCardInfoFrame = SocialFrames.Prefix(info);
        LastCardViewFrame = SocialFrames.Prefix(view);
        LastEquipmentFrame = SocialFrames.Prefix(equipment);
        bool any = false;
        bool ok = true;
        bool injected = Inject is not null;
        foreach (byte[] frame in new[] { LastCardInfoFrame, LastCardViewFrame, LastEquipmentFrame })
        {
            if (frame.Length == 0) continue;
            any = true;
            if (!injected) continue;
            try { ok &= Inject!(frame); }
            catch { ok = false; }
        }
        string status = !any ? "Le code du message est absent du mapping."
            : !injected ? ProfileText.FrameReady
            : ok ? ProfileText.ProfileSent
            : "L'injection a été refusée.";
        int gen = CurrentGeneration();
        Run(() =>
        {
            if (gen != CurrentGeneration()) return;
            if (!string.IsNullOrWhiteSpace(name)) ProfileName = name!;
            ShownName = name ?? "";
            ProfileStatus = status;
            Publish();
        });
    }

    public void IngestCardInfo(IReadOnlyList<ProtoField> payload)
    {
        var header = PlayerProfileReader.ReadInfo(payload, FieldNumber);
        bool failed = header is null && SocialFields.Node(payload, SocialFields.N(FieldNumber, "CardInfoResponse", "failed", 1)) is not null;
        int gen = CurrentGeneration();
        lock (_gate)
        {
            if (gen != _generation) return;
            if (header is not null) _memory.ApplyInfo(header);
        }
        Run(() =>
        {
            if (CurrentGeneration() != gen) return;
            if (failed) ProfileStatus = ProfileText.ProfileMissing;
            else if (header is not null)
            {
                if (header.Name.Length > 0) ShownName = header.Name;
                Publish();
            }
        });
    }

    public void IngestCardView(IReadOnlyList<ProtoField> payload)
    {
        var body = PlayerProfileReader.ReadView(payload, FieldNumber);
        int gen = CurrentGeneration();
        bool failedBeforeGeneral = false;
        lock (_gate)
        {
            if (gen != _generation) return;
            failedBeforeGeneral = body.Failed && !_memory.GeneralReceived;
            _memory.ApplyView(body);
        }
        Run(() =>
        {
            if (CurrentGeneration() != gen) return;
            if (failedBeforeGeneral) ProfileStatus = ProfileText.ProfileMissing;
            Publish();
        });
    }

    public void IngestChatEvent(IReadOnlyList<ProtoField> payload) => AddChat(ChatMessages.ReadEvent(payload, FieldNumber));

    public void IngestChannelUpdate(IReadOnlyList<ProtoField> payload)
    {
        var toggle = ChatMessages.ReadUpdate(payload, FieldNumber);
        Run(() => LastChannelChange = (toggle.Enable ? "Activé" : "Coupé") + " : " + ChannelPlan.Label(toggle.Channel) + ".");
    }

    public void NoteOutgoingSubscribe(IReadOnlyList<ProtoField> payload)
    {
        var (active, disabled) = ChannelPlan.Read(payload, FieldNumber);
        string summary = ChannelPlan.Summary(active, disabled);
        Run(() => ServerChannels = summary);
    }

    public void AddObservedChat(int channel, string sender, long senderId, string content)
    {
        var chat = new CapturedChat(channel, content ?? "", sender ?? "", senderId, 0, Array.Empty<ChatItemLink>(), Array.Empty<MonsterGroupCard>());
        AddChat(chat);
    }

    [RelayCommand(CanExecute = nameof(CanSubscribeAll))]
    private void SubscribeAll()
    {
        if (!ChannelPlan.MaySend(IsMitm, AllowGameChannelCheckboxes)) return;
        byte[] message = SocialFrames.SubscribeMessage(CodeForName, FieldNumber, ChannelPlan.AllChannels, Array.Empty<int>(), RequestUid);
        LastSubscribeFrame = SocialFrames.Prefix(message);
        if (message.Length == 0)
        {
            ChannelStatus = "Le code du message est absent du mapping.";
            return;
        }
        if (Inject is null)
        {
            ChannelStatus = ChannelPlan.SubscribeReady;
            return;
        }
        bool ok;
        try { ok = Inject(LastSubscribeFrame); }
        catch { ok = false; }
        ChannelStatus = !ok ? "L'injection a été refusée." : IsMitm ? ChannelPlan.BoxesWillChange : ChannelPlan.SubscribeSent;
    }

    private void AddChat(CapturedChat chat)
    {
        int gen = CurrentGeneration();
        Run(() =>
        {
            if (gen != CurrentGeneration()) return;
            lock (_gate)
            {
                if (gen != _generation) return;
                _directory.Remember(chat.Sender, chat.SenderCharacterId);
                foreach (var link in ChatLinks.Links(chat.Content))
                    if (link.Kind == "player") _directory.Remember(link.Text, link.A);
            }
            EnsureChannel(chat.Channel);
            _all.Add(BuildLine(chat));
            while (_all.Count > 400) _all.RemoveAt(0);
            RefreshShown();
        });
    }

    private SocialChatLine BuildLine(CapturedChat chat)
    {
        string text = ChatLinks.Readable(chat.Content, gid => Names.ItemName(gid));
        var links = new List<SocialLinkVm>();
        foreach (var link in ChatLinks.Links(chat.Content))
        {
            if (link.Kind == "player")
            {
                string player = link.Text;
                long id = link.A;
                links.Add(new SocialLinkVm("Profil de " + player, () => RequestCharacter(id, player), null, null));
            }
            else if (link.Kind == "item")
            {
                ChatItemLink? known = null;
                foreach (var item in chat.Items)
                    if (item.Uid == link.A && item.Gid == link.B) { known = item; break; }
                var card = known is null
                    ? HoverCards.Item(link.B, Names, AveragePrice?.Invoke(link.B), null, EffectTemplate)
                    : HoverCards.Item(known, Names, AveragePrice, EffectTemplate);
                string label = card.Name;
                links.Add(new SocialLinkVm(label, () => { SelectedMonster = null; SelectedItem = card; }, card, null));
            }
            else if (link.Kind == "monster")
            {
                MonsterGroupCard? group = null;
                foreach (var candidate in chat.Groups)
                    if (candidate.GroupId == link.A) { group = candidate; break; }
                group ??= new MonsterGroupCard(link.A, null, null, null, null, null, Array.Empty<GroupMember>());
                var card = HoverCards.Monster(group, Names);
                links.Add(new SocialLinkVm("Groupe", () => { SelectedItem = null; SelectedMonster = card; }, null, card));
            }
        }
        bool hasSender = chat.Sender.Length > 0;
        string senderLabel = hasSender ? "Profil de " + chat.Sender : "";
        ICommand? openSender = null;
        if (hasSender)
        {
            string sender = chat.Sender;
            long senderId = chat.SenderCharacterId;
            openSender = new RelayCommand(() =>
            {
                if (senderId > 0) RequestCharacter(senderId, sender);
                else
                {
                    ProfileName = sender;
                    RequestProfile();
                }
            });
        }
        return new SocialChatLine(chat.Channel, "[" + ChannelPlan.Label(chat.Channel) + "] " + chat.Sender, text, senderLabel, hasSender, openSender, links);
    }

    private void EnsureChannel(int id)
    {
        foreach (var channel in Channels)
            if (channel.Id == id) return;
        Channels.Add(new ChannelFilter(id, true, RefreshShown));
    }

    private void RefreshShown()
    {
        var selected = new HashSet<int>();
        foreach (var channel in Channels)
            if (channel.Shown) selected.Add(channel.Id);
        Messages.Clear();
        foreach (var line in _all)
            if (ChannelPlan.Shows(line.Channel, selected)) Messages.Add(line);
    }

    private void Publish()
    {
        CharacterHeader? header;
        SocialData? social;
        EquipmentCardData? equipment;
        EquipmentState state;
        lock (_gate)
        {
            header = _memory.Header;
            social = _memory.Social;
            equipment = _memory.Equipment;
            state = _memory.EquipmentState;
        }
        if (header is { Name.Length: > 0 }) ShownName = header.Name;
        Rows.Clear();
        foreach (var row in ProfileText.General(header, social, Names)) Rows.Add(row);
        Equipment.Clear();
        EquipmentNote = ProfileText.Note(state);
        HasEquipmentNote = EquipmentNote.Length > 0;
        if (state == EquipmentState.Shown && equipment is not null)
            foreach (var row in ProfileText.Equipment(equipment, Names, AveragePrice, EffectTemplate))
                Equipment.Add(row);
    }

    private int CurrentGeneration()
    {
        lock (_gate) return _generation;
    }

    private void Run(Action action)
    {
        var dispatcher = Dispatcher.UIThread;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Post(action);
    }
}
