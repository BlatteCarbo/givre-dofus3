using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

public partial class AccountsViewModel
{
    public CharacterCreationTabViewModel CharacterCreation { get; private set; } = null!;

    [ObservableProperty] public partial bool CharacterCreationOpen { get; set; }

    [RelayCommand]
    private async Task OpenCharacterCreationAsync()
    {
        var selected = SelectedRows.ToList();
        if (selected.Count != 1)
        { Toasts.Show(Loc.T("L.Socket.Create.SelectAccount"), success: false); return; }
        await StartCharacterCreationAsync(selected[0]);
    }

    /// <summary>Ouvre l'écran de choix Socket de ce compte directement sur « Nouveau personnage ».</summary>
    public async Task StartCharacterCreationAsync(AccountRow row)
    {
        if (row.Running)
        { Toasts.Show(Loc.T("L.Socket.Create.StopAccount"), success: false); return; }
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();
        if (row.Running) return;
        StartSocketCore(row, unattendedShield: null, character: null, allowSelect: true, createCharacter: true);
    }

    internal void CloseCharacterCreation() => CharacterCreationOpen = false;

    internal IReadOnlyList<CreationAccountChoice> CreationAccounts()
        => Accounts.Select(row =>
        {
            bool liveSocket = row.Running && row.IsSocketSession;
            bool remembered = row.IsSocketMode;
            string mode = liveSocket || remembered ? "Socket" : "MITM";
            string online = row.Running ? " · en ligne" : "";
            return new CreationAccountChoice
            {
                Login = row.Login,
                SocketSession = liveSocket,
                RememberedSocket = remembered,
                Running = row.Running,
                Pid = row.Pid ?? 0,
                Label = row.Login + " · " + mode + online,
            };
        }).ToList();

    internal IReadOnlyList<(int Id, string Name)> CreationServerList()
    {
        try { return _launch.GameNames.KnownServers(); }
        catch { return Array.Empty<(int, string)>(); }
    }

    internal bool CreationOnList(int pid) => pid > 0 && _launch.IsOnCharacterList((uint)pid);

    internal CreationFieldPresence CreationFields()
    {
        try
        {
            var cat = MappingCatalog.GetEffective();
            return cat is null ? CreationFieldPresence.None : CharacterCreationTab.ReadFields(cat.CodeForName, cat.FieldNumber);
        }
        catch { return CreationFieldPresence.None; }
    }

    internal (int Head, int Body, int[] Defaults, string? Error) CreationAppearance(int breedIndex, int sex)
    {
        if (CharacterInfoReader.BreedName(breedIndex) is null || sex is not (0 or 1))
            return (0, 0, Array.Empty<int>(), null);
        try
        {
            if (_launch.GameData is not { } db)
                return (0, 0, Array.Empty<int>(), "Données du jeu indisponibles (dofus.sqlite).");
            int breedId = ClassIconProvider.BreedId(breedIndex);
            var heads = db.CreationHeads(breedId, sex);
            var bodies = db.CreationBodies(breedId, sex);
            if (heads.Count == 0 || bodies.Count == 0)
                return (0, 0, Array.Empty<int>(), "Tête ou corps de création introuvable pour cette classe.");
            return (heads[0].Id, bodies[0].Id, db.BreedDefaultColors(breedId, sex).ToArray(), null);
        }
        catch
        {
            return (0, 0, Array.Empty<int>(), "Données du jeu indisponibles (dofus.sqlite).");
        }
    }

    internal Task<MitmCreationResult> SendCreationOnListAsync(int pid, CharacterCreationSpec spec)
        => _launch.CreateOnCharacterListAsync((uint)pid, spec);

    /// <summary>Connexion Socket existante : la création part à la liste. Le mode mémorisé du compte n'est pas modifié.</summary>
    internal bool BeginCharacterCreationSocket(string login, CharacterCreationSpec spec, string serverKey)
    {
        var row = Accounts.FirstOrDefault(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));
        if (row is null || row.Running) return false;
        SocketCreationRegistry.Set(row.Login, spec);
        SocketScriptedPickRegistry.Set(row.Login, new ScriptedPick(serverKey, null));
        bool started = false;
        try { started = StartSocketCore(row, unattendedShield: null, character: null, allowSelect: false); }
        catch { started = row.Running; }
        if (!started) ClearCreationRegistries(row.Login);
        return started;
    }

    private static void ClearCreationRegistries(string login)
    {
        SocketCreationRegistry.Clear(login);
        SocketScriptedPickRegistry.Clear(login);
    }
}
