using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;

namespace GivreClone.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Behaviors.AccessibleNameBehavior.Install();   // boutons a contenu compose : nom lisible pour lecteurs d'ecran / automation
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before any clone configuration or TEST-profile store is opened:
            // repair inherited ACLs on this app's exact isolated TEST run only.
            if (MZP19LocalClone.Enabled) MZP19LocalClone.PreparePrivateRun();
            GivreClone.App.Services.LocalLaunch.AnkamaOAuth.Device.DataDirProvider = () => MZP19LocalClone.DataRoot;
            // Restauration de réglages préparée depuis les Réglages : appliquée AVANT tout chargement de store.
            string? restoreNotice = Services.SettingsBackup.ApplyPending(MZP19LocalClone.DataRoot);
            // Ordre important : config -> langue -> thème -> audio avant de construire la fenêtre.
            AppConfig.Load();
            CombatProfileStore.Load();   // profils de combat par personnage (%APPDATA%\GivreClone\combat.json)
            PlanningStore.Load();        // plannings hebdo par personnage (%APPDATA%\GivreClone\planning.json)
            BotSettingsStore.Load();     // réglages par personnage : déplacement + messagerie (%APPDATA%\GivreClone\bot-settings.json)
            HdvProfileStore.Load();      // config HDV par personnage (%APPDATA%\GivreClone\hdv.json)
            Services.Combat.CombatJournalService.Init();   // replay de combat (lit le même flux « [combat] »)
            TransportDiag.Init();   // capte le transport (HandshakeRelay) vers %APPDATA%\GivreClone\logs\transport-<session>.log
            Loc.Apply(AppConfig.Current.Language); // sinon {DynamicResource L.*} = clé brute
            // "Sans proxy" (sentinelle ProxyEntry.None) : le modèle vit dans l'Engine (sans accès à Loc) -> on
            // localise son libellé ici, au démarrage puis à chaque changement de langue.
            GivreClone.Engine.Proxies.ProxyEntry.None.Name = Loc.T("L.Proxys.None");
            Loc.Changed += () => GivreClone.Engine.Proxies.ProxyEntry.None.Name = Loc.T("L.Proxys.None");
            ThemeService.Apply(AppConfig.Current.Theme);
            SkinService.Apply(AppConfig.Current.UiSkin);   // skin un bot tiers (gris/violet plat) si active: AVANT la fenêtre
            AudioService.ApplyFromConfig();
            var mainWindow = new MainWindow { DataContext = new MainViewModel() };
            desktop.MainWindow = mainWindow;
            // Fichier de réglages illisible au chargement (mis de côté, jamais écrasé) : signalé une fois la fenêtre ouverte
            // (les stores sont chargés avant elle, personne n'écoutait encore les toasts).
            mainWindow.Opened += (_, _) =>
            {
                if (restoreNotice is not null) Toasts.Show(restoreNotice, success: !restoreNotice.Contains("impossible"));
                while (AtomicJson.StoreNotices.TryDequeue(out var n))
                    Toasts.Show(n.BadCopy is { } bad
                                    ? Loc.Fmt(n.Restored ? "L.Store.Restored" : "L.Store.Quarantined", n.File, bad)
                                    : Loc.Fmt(n.Restored ? "L.Store.UnreadableRestored" : "L.Store.Unreadable", n.File),
                                success: false);

                // Démarrage automatique des comptes activés avec AutoConnectType=2 au lancement de l'application
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(1500);
                    if (mainWindow.DataContext is MainViewModel mv)
                    {
                        foreach (var row in mv.AccountsVM.Accounts.Where(a => a.Model.Enabled && a.Model.AutoConnectType == 2 && !a.Running).ToList())
                        {
                            _ = mv.AccountsVM.LaunchInGameForTeamAsync(row);
                            await Task.Delay(500);
                        }
                    }
                });
            };
            Stores.UiIcons.KickBackgroundLoad();   // préchauffe les icônes UI du jeu HORS thread UI -> l'onglet Bots ne gèle plus au 1er clic
            int exitDone = 0;
            void RunExitTasks()
            {
                if (System.Threading.Interlocked.Exchange(ref exitDone, 1) != 0) return;   // une seule fois (les deux événements)
                (mainWindow.DataContext as MainViewModel)?.LocalControl?.Stop();
                try { Services.Combat.CombatJournalService.FlushAll(); } catch { /* best-effort */ }   // combats encore ouverts -> journal
                // Nom de script de planning en cours de frappe (validé à Entrée / perte de focus) : enregistré, pas perdu à la fermeture.
                try { (mainWindow.DataContext as MainViewModel)?.BotsVM.CommitPendingPlanningEdits(); } catch { /* best-effort */ }
                Stores.Stats.FlushAll();      // persiste les stats en attente a la fermeture
                if (!MZP19LocalClone.Enabled) LicenseService.LogoutBlocking();
                TransportDiag.Flush();        // journal transport écrit par lots : vide la file (dernières lignes de session)
            }
            desktop.ShutdownRequested += (_, _) => RunExitTasks();
            // Shutdown() FORCÉ (« Redémarrer » des Réglages, mise à jour obligatoire) : Avalonia ne lève PAS ShutdownRequested ->
            // sans ce filet, ni déconnexion de licence (session fantôme dans le panel) ni sauvegarde des stats en attente.
            desktop.Exit += (_, _) => RunExitTasks();
        }
        base.OnFrameworkInitializationCompleted();
        Services.MemoryTrimmer.Start();
    }
}
