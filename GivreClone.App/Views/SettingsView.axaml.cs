using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class SettingsView : UserControl
{
    private bool _ready;

    public SettingsView()
    {
        InitializeComponent();
        SfxToggle.IsChecked = AppConfig.Current.Sfx;
        SfxVolumeSlider.Value = Math.Clamp(AppConfig.Current.SfxVolume, 0f, 1f) * 100;   // 0..1 -> 0..100
        SfxVolumeSlider.IsEnabled = AppConfig.Current.Sfx;
        AmbientToggle.IsChecked = AppConfig.Current.Ambient;
        RemoteToggle.IsChecked = AppConfig.Current.RemoteEnabled;
        RemoteLanToggle.IsChecked = AppConfig.Current.RemoteAllowLan;
        RefreshRemote();
        ParticlesToggle.IsChecked = AppConfig.Current.Particles;
        SeasonalToggle.IsChecked = AppConfig.Current.SeasonalDecor;
        ModsToggle.IsChecked = AppConfig.Current.ModsEnabled;
        RefreshNativeMods();
        InitializeItemOverlaySettings();
        AutoRelaunchToggle.IsChecked = AppConfig.Current.AutoRelaunch;
        StreamToggle.IsChecked = AppConfig.Current.StreamMode;
        PauseTurnsBox.Value = AppConfig.Current.PauseFightAfterTurns;
        AbandonTurnsBox.Value = AppConfig.Current.AbandonFightAfterTurns;
        FightPauseMinBox.Value = AppConfig.Current.FightPauseMinSeconds;
        FightPauseMaxBox.Value = AppConfig.Current.FightPauseMaxSeconds;
        ArchDmToggle.IsChecked = AppConfig.Current.ArchDiscordDm;
        RichPresenceToggle.IsChecked = AppConfig.Current.RichPresence;
        SkinToggle.IsChecked = AppConfig.Current.UiSkin == "classic";
        PacketDumpToggle.IsChecked = AppConfig.Current.PacketDump;
        PacketDumpAllToggle.IsChecked = AppConfig.Current.PacketDumpAll;
        ApplyDevOnlyCards();
        Highlight(ThemeService.CurrentCode);
        HighlightDensity(AppConfig.Current.Density);
        HighlightLang(Loc.CurrentCode);
        _ready = true;   // évite que l'init ci-dessus ne déclenche les handlers
    }

    private void OnPickLang(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is Button { Tag: string code }) { Loc.Set(code); HighlightLang(code); }
    }

    private void HighlightLang(string code)
    {
        foreach (var b in LangSwatches.Children.OfType<Button>())
        {
            b.Classes.Remove("active");
            if ((b.Tag as string) == code) b.Classes.Add("active");
        }
    }

    private void OnSeasonalToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.SeasonalDecor = SeasonalToggle.IsChecked == true;
        AppConfig.Save();
    }

    private void OnParticlesToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.Particles = ParticlesToggle.IsChecked == true;
        AppConfig.Save();
        (TopLevel.GetTopLevel(this) as MainWindow)?.ApplyParticles();
    }

    private void OnPickDensity(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not Button { Tag: string code }) return;
        AppConfig.Current.Density = code;
        AppConfig.Save();
        HighlightDensity(code);
        (TopLevel.GetTopLevel(this) as MainWindow)?.ApplyDensity();
    }

    private void HighlightDensity(string code)
    {
        foreach (var b in DensitySwatches.Children.OfType<Button>())
        {
            b.Classes.Remove("active");
            if ((b.Tag as string) == code) b.Classes.Add("active");
        }
    }

    private void OnPickTheme(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is Button { Tag: string code })
        {
            ThemeService.Set(code);
            Highlight(code);
        }
    }

    private void Highlight(string code)
    {
        foreach (var b in ThemeSwatches.Children.OfType<Button>())
        {
            b.Classes.Remove("active");
            if ((b.Tag as string) == code) b.Classes.Add("active");
        }
    }

    private void OnSfxToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AudioService.SetSfx(SfxToggle.IsChecked == true);
        SfxVolumeSlider.IsEnabled = SfxToggle.IsChecked == true;   // le curseur de volume suit l'activation des sons
    }

    private void OnSfxVolumeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        AudioService.SetSfxVolume((float)(SfxVolumeSlider.Value / 100.0));   // 0..100 (UI) -> 0..1 (moteur)
    }

    // Injection des mods natifs (GivreMods.dll) : ON = injecte au lancement d'un client ; OFF = client vanilla.
    // Prend effet au PROCHAIN lancement (les clients deja ouverts gardent leur etat d'injection).
    private void OnModsToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.ModsEnabled = ModsToggle.IsChecked == true;
        AppConfig.Save();
    }

    private void OnRefreshNativeMods(object? sender, RoutedEventArgs e) => RefreshNativeMods();

    private void RefreshNativeMods()
        => NativeModHealth.Text = MZP25NativeModCatalog.Summary(
            System.IO.Path.Combine(AppContext.BaseDirectory, "SourceOwnedMods"));

    // Relance auto : ON = si un client lancé par GivreClone meurt de façon inattendue (crash instantané / fermeture
    // externe), on le relance tout seul (borné par un budget anti-boucle). Prend effet aux prochains crashs.
    private void OnAutoRelaunchToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.AutoRelaunch = AutoRelaunchToggle.IsChecked == true;
        AppConfig.Save();
    }

    // MP Discord d'archimonstres : ON = je reçois un MP Discord quand un de mes persos trouve un archi. OFF = le client
    // envoie no_dm -> le backend ne me DM pas, mais la détection remonte quand même (salon public + admin). Alerte en jeu inchangée.
    private void OnArchDmToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.ArchDiscordDm = ArchDmToggle.IsChecked == true;
        AppConfig.Save();
    }

    // Rejoindre le Discord GivreClone (invitation ouverte dans le navigateur).
    private void OnJoinDiscord(object? sender, RoutedEventArgs e) => Links.OpenDiscord();

    // Rich Presence Discord : ON = « Joue à GivreClone » (nom + libellé + chrono + boutons) sur ton profil Discord.
    // Prend effet TOUT DE SUITE (démarre/arrête la présence). No-op tant qu'aucun Application ID n'est renseigné.
    private void OnRichPresenceToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.RichPresence = RichPresenceToggle.IsChecked == true;
        AppConfig.Save();
        DiscordRichPresence.ApplyFromConfig();
    }

    private void RefreshRemote()
    {
        bool on = AppConfig.Current.RemoteEnabled;
        RemoteDetails.IsVisible = on;
        if (!on) return;
        RemoteTokenBox.Text = AppConfig.Current.RemoteToken;
        var addr = MainViewModel.RemoteAddresses();
        RemoteAddresses.Text = addr.Count > 0 ? string.Join("\n", addr) : "Aucune adresse Tailscale détectée : installe Tailscale sur le PC et le téléphone (ou autorise le réseau local).";
    }

    private void OnRemoteToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool want = RemoteToggle.IsChecked == true;
        if (MainViewModel.Instance is { } vm && !vm.SetRemote(want))
        {
            RemoteToggle.IsChecked = false;
            Toasts.Show($"Impossible d'ouvrir le port {AppConfig.Current.RemotePort} (déjà utilisé ?).", success: false);
        }
        RefreshRemote();
    }

    private void OnRemoteLanToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.RemoteAllowLan = RemoteLanToggle.IsChecked == true;
        AppConfig.Save();
        RefreshRemote();
    }

    private async void OnRemoteCopyToken(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(AppConfig.Current.RemoteToken); Toasts.Show("Jeton copié.", success: true); }
    }

    private void OnRemoteNewToken(object? sender, RoutedEventArgs e)
    {
        AppConfig.Current.RemoteToken = GivreClone.App.Services.Mcp.RemoteAccessPolicy.NewToken();
        AppConfig.Save();
        RefreshRemote();
        Toasts.Show("Nouveau jeton créé : le téléphone devra le ressaisir.", success: true);
    }

    private void OnAmbientToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AudioService.SetAmbient(AmbientToggle.IsChecked == true);
    }

    // Skin de l'interface: ON = thème COMPLET (gris/violet plat + barre de menu + arbre chef/mules),
    // OFF = interface GivreClone. Persiste le choix, puis propose un REDÉMARRAGE : le thème complet (palette de base
    // en StaticResource figée au chargement) ne s'applique proprement qu'au relancement.
    private void OnSkinToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool snow = SkinToggle.IsChecked == true;
        MainViewModel.Instance?.SetSkin(snow);
        Dialogs.Confirm(
            Loc.T("L.Settings.SkinRestart.Title"),
            Loc.T("L.Settings.SkinRestart.Body"),
            RestartApp,
            Loc.T("L.Settings.SkinRestart.Ok"));
    }

    // Relance l'exe puis ferme l'instance courante (l'app tourne en AOT/Release et redémarre proprement).
    // « Plus tard » ne fait rien : le thème s'appliquera au prochain lancement (le choix est déjà persisté).
    private static void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); } catch { }
        }
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void OnExportSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            AppConfig.Save();
            string zip = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"GivreClone-reglages-{DateTime.Now:yyyyMMdd-HHmm}.zip");
            int n = SettingsBackup.Export(MZP19LocalClone.DataRoot, zip, BackupSecretsBox.IsChecked == true);
            BackupStatus.Text = $"{n} fichier(s) exporté(s) : {System.IO.Path.GetFileName(zip)} (Bureau).";
        }
        catch (Exception ex) { BackupStatus.Text = "Export impossible : " + ex.Message; }
    }

    private async void OnImportSettings(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Zip de réglages GivreClone",
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Zip") { Patterns = new[] { "*.zip" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            SettingsBackup.Stage(path, MZP19LocalClone.DataRoot);
            BackupStatus.Text = "Restauration prête : elle s'applique au redémarrage.";
            Dialogs.Confirm("Restaurer les réglages", "Les réglages seront remplacés au redémarrage (l'ancienne version est gardée dans le dossier backups). Redémarrer maintenant ?",
                RestartApp, "Redémarrer");
        }
        catch (Exception ex) { BackupStatus.Text = "Restauration impossible : " + ex.Message; }
    }

    private void OnFightTurnsChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.PauseFightAfterTurns = (int)(PauseTurnsBox.Value ?? 0);
        AppConfig.Current.AbandonFightAfterTurns = (int)(AbandonTurnsBox.Value ?? 0);
        AppConfig.Current.FightPauseMinSeconds = (int)(FightPauseMinBox.Value ?? 0);
        AppConfig.Current.FightPauseMaxSeconds = (int)(FightPauseMaxBox.Value ?? 0);
        AppConfig.Save();
    }

    // Mode Recording/Stream : floute toute PII de l'UI (identifiant/personnage/serveur/IP/HWID) pour une démo/stream.
    private void OnStreamToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.StreamMode = StreamToggle.IsChecked == true;
        AppConfig.Save();
        (TopLevel.GetTopLevel(this) as MainWindow)?.ApplyStreamMode();
    }

    // Captures de diagnostic = outils de reverse (arbres de champs, trames brutes, codes) : visibles et ACTIFS seulement avec la
    // licence développeur. Hors développeur elles sont masquées et le moteur les ignore même si le réglage est resté sur On.
    // Droits de licence changés en direct (login, reprise, panel) : les outils développeur apparaissent / disparaissent. Abonné
    // seulement tant que la vue est affichée (une vue est recréée à chaque visite : un abonnement au constructeur fuyait).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LicenseService.PermissionsChanged += OnPermissionsChanged;
        ApplyDevOnlyCards();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LicenseService.PermissionsChanged -= OnPermissionsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPermissionsChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyDevOnlyCards);

    private void ApplyDevOnlyCards()
    {
        bool dev = LicenseService.IsDeveloper;
        PacketDumpCard.IsVisible = dev;
        PacketDumpAllCard.IsVisible = dev;
    }

    // Capture de découverte : dumpe l'arbre de champs des messages non-mappés / d'échange dans transport-*.log
    // (diagnostic reverse HDV). Lu à chaud par LaunchService.OnDecoded via AppConfig.Current.PacketDump.
    private void OnPacketDumpToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.PacketDump = PacketDumpToggle.IsChecked == true;
        AppConfig.Save();
    }

    // Capture exhaustive : leve le filtre et dumpe AUSSI les messages deja nommes. Indispensable apres
    // un patch Dofus, ou un code mal re-lie porte un nom faux et echapperait donc au journal.
    private void OnPacketDumpAllToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppConfig.Current.PacketDumpAll = PacketDumpAllToggle.IsChecked == true;
        AppConfig.Save();
    }
}
