using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Controls;

/// <summary>
/// Standalone, opt-in diagnostic page. Constructing it never attaches to a client or process.
/// The host must supply a verified observation source before the first real event can appear.
/// </summary>
public partial class MZP0OperatorPanel : UserControl, IDisposable
{
    private readonly DispatcherTimer _expiryTimer;
    private bool _disposed;

    public MZP0OperatorViewModel Operator { get; } = new();

    public MZP0OperatorPanel()
    {
        InitializeComponent();
        DataContext = Operator;
        _expiryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _expiryTimer.Tick += OnExpiryTick;
        AttachedToVisualTree += (_, _) => { if (!_disposed) _expiryTimer.Start(); };
        DetachedFromVisualTree += (_, _) =>
        {
            _expiryTimer.Stop();
            Operator.Unbind();
        };
    }

    /// <summary>
    /// Explicit integration seam for PRIME: source and separate trusted authority plus
    /// the exact session identity (PID/birth/generation/build/SHA) pinned by the host.
    /// </summary>
    public void Bind(IMZP0OperatorObservationSource source, IMZP0OperatorObservationAuthority authority,
        MZP0SessionIdentity pinnedSession, string approvedSourceId)
        => Operator.Bind(source, authority, pinnedSession, approvedSourceId);

    private void OnExpiryTick(object? sender, EventArgs args) => Operator.RecheckExpiry();

    private void OnRefreshLocal(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Les artefacts locaux ne prouvent ni la version exacte du client actif,
        // ni une admission protocolaire, ni la provenance d'un paquet.
        LocalVersion.Text = "Client : version installée non certifiée · admission version refusée (R3)";
        try
        {
            var launch = Stores.Launch;
            LocalProtocol.Text = launch.Catalog is null
                ? "Protocole : catalogue absent · admission runtime inconnue"
                : "Protocole : catalogue chargé (candidat) · admission runtime NON ATTESTÉE";
            LocalGameDb.Text = launch.GameData is { } db
                ? "GameDb : base SQLite locale accessible en lecture seule (version des données indépendante du client)"
                : "GameDb : aucune base locale accessible";
            var maps = launch.MapCacheManifestSummary();
            LocalMaps.Text = launch.MapDataAvailable
                ? $"Cartes : bundles accessibles · manifeste {maps.DeclaredMaps} carte(s) déclarée(s) · " +
                  (maps.DeclaredComplete ? "extraction complète déclarée" : "extraction incomplète ou inconnue") +
                  " · intégrité/actualité non attestées"
                : "Cartes : bundles introuvables · nombre extrait non attesté";
            LocalTransport.Text = string.IsNullOrEmpty(TransportDiag.Path_)
                ? "Transport : journal non initialisé · session / messages / ACK non attestés"
                : "Transport : journal local initialisé · connexion / messages / ACK non attestés";
        }
        catch
        {
            LocalProtocol.Text = "Protocole : diagnostic local indisponible";
            LocalGameDb.Text = "GameDb : diagnostic local indisponible";
            LocalMaps.Text = "Cartes : diagnostic local indisponible";
            LocalTransport.Text = "Transport : diagnostic local indisponible";
        }
    }

    private void OnInspectHdv(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!uint.TryParse(HdvPid.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint pid) || pid == 0 ||
            !long.TryParse(HdvGid.Text, NumberStyles.None, CultureInfo.InvariantCulture, out long gid) || gid <= 0)
        {
            HdvResult.Text = "PID ou GID invalide : entrer deux identifiants numériques positifs.";
            return;
        }
        try
        {
            var launch = Stores.Launch;
            int serverId = launch.ServerIdOf(pid);
            string server = serverId > 0 ? serverId.ToString(CultureInfo.InvariantCulture) : "inconnu";
            var prices = launch.Hdv.MarketPricesFor(pid, gid);
            var sizes = launch.HdvLotSizes(pid);
            long age = launch.Hdv.PriceAgeMs(pid, gid);
            string when = age < long.MaxValue && age >= 0
                ? DateTimeOffset.Now.AddMilliseconds(-age).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
                : "inconnu";
            string freshness = age < long.MaxValue && age >= 0 ? $"{age / 1000}s" : "inconnue";
            string[] lines = prices.Select((price, slot) =>
                $"lot x{(slot < sizes.Length ? sizes[slot].ToString(CultureInfo.InvariantCulture) : "?")} : " +
                (price > 0 ? $"{price} kamas · source=min marché en cache · vu={when} · âge={freshness}" :
                             "prix inconnu · origine=aucune observation exploitable")).ToArray();
            long mean = launch.Hdv.AverageFor(pid, gid);
            HdvResult.Text = $"PID {pid} · serveur {server} · GID {gid}\n" +
                (lines.Length == 0 ? "Aucun relevé du marché en cache.\n" : string.Join("\n", lines) + "\n") +
                (mean > 0 ? $"Prix moyen : {mean} kamas · source=moyenne en cache · horodatage inconnu" :
                            "Prix moyen : inconnu · origine absente") +
                "\nLes valeurs sont des relevés locaux, sans vérification de disponibilité à l'instant T.";
        }
        catch { HdvResult.Text = "Relevé indisponible : aucune estimation vérifiable."; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _expiryTimer.Stop();
        _expiryTimer.Tick -= OnExpiryTick;
        Operator.Dispose();
    }
}
