using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Engine.Proxies;

namespace GivreClone.App.ViewModels;

public partial class ProxyItem
{
    /// <summary>Texte « N compte(s) » : combien de comptes utilisent ce proxy (vide si aucun). Déduit du nom du membre de l'original.</summary>
    public string ByTeam => AccountCount switch { 0 => "", 1 => "1 compte", var n => $"{n} comptes" };
    public bool HasByTeam => AccountCount > 0;
    private int AccountCount => Stores.Accounts.Accounts.Count(a => a.ProxyId == Model.Id);

    /// <summary>Pourquoi la suppression est impossible (vide si elle est possible). Partage GivreRelay encore listé : géré depuis le portail.</summary>
    public string BlockedText => CanRemove ? "" : "Partage GivreRelay : à retirer depuis le portail du relais.";
    public bool IsBlocked => !CanRemove;
}

// Import en masse de proxys (texte collé). Analyse pure dans ProxyImportParser ; ici : état de l'assistant et ajout au magasin.
public partial class ProxysViewModel
{
    [ObservableProperty] public partial bool ImportVisible { get; set; }
    [ObservableProperty] public partial string ImportText { get; set; } = "";
    [ObservableProperty] public partial string ImportProtocol { get; set; } = "SOCKS5";
    [ObservableProperty] public partial string ImportNameBase { get; set; } = "Proxy";
    [ObservableProperty] public partial bool ImportTest { get; set; }

    private ProxyImportResult _importParsed = ProxyImportParser.Parse(null);
    private MZP24ProxyReviewResult _importReview = MZP24ProxyImportReview.Inspect(null);
    private string _importReviewIssue = "";

    /// <summary>Résumé et diagnostics par ligne, sans identifiants ni mot de passe.</summary>
    public string ImportPreview => string.IsNullOrWhiteSpace(ImportText) ? "" :
        _importReviewIssue.Length > 0 ? _importReviewIssue :
        _importReview.Summary + (_importReview.Rejected > 0 ? " · Corrigez les erreurs avant d'importer." : "");
    public IReadOnlyList<MZP24ProxyReviewRow> ImportRows => _importReview.Rows;
    public bool ImportReady => _importReviewIssue.Length == 0 &&
        _importReview.Accepted > 0 && _importReview.Rejected == 0;

    partial void OnImportTextChanged(string value) => ReparseImport();
    partial void OnImportProtocolChanged(string value) => ReparseImport();

    private void ReparseImport()
    {
        try
        {
            // Review first: the parser alone accepts ambiguous HTTPS and malformed
            // hosts. Refusing any invalid line prevents a partial unsafe import.
            _importReview = MZP24ProxyImportReview.Inspect(ImportText, ImportProtocol);
            _importParsed = _importReview.Rejected == 0
                ? ProxyImportParser.Parse(ImportText, ImportProtocol)
                : ProxyImportParser.Parse(null);
            _importReviewIssue = "";
        }
        catch (System.ArgumentException)
        {
            _importReview = MZP24ProxyImportReview.Inspect(null);
            _importParsed = ProxyImportParser.Parse(null);
            _importReviewIssue = "Texte trop long ou protocole invalide. Limites : 262144 caractères et 4096 lignes.";
        }
        OnPropertyChanged(nameof(ImportPreview));
        OnPropertyChanged(nameof(ImportRows));
        OnPropertyChanged(nameof(ImportReady));
    }

    [RelayCommand]
    private void OpenImport()
    {
        ImportText = ""; ImportTest = false; ImportNameBase = "Proxy";
        ImportVisible = true;
    }

    [RelayCommand] private void CancelImport() { ImportVisible = false; ImportText = ""; }

    [RelayCommand]
    private async Task ImportProxies()
    {
        ReparseImport(); // validate the latest text again at commit
        if (!ImportReady) { Toasts.Show("Aucun proxy valide à importer.", success: false); return; }
        int i = 0;
        var added = new System.Collections.Generic.List<ProxyEntry>();
        foreach (var p in _importParsed.Valid)
        {
            var e = Stores.Proxies.AddExternal(ProxyImportParser.NameFor(ImportNameBase, ++i),
                p.Protocol == "HTTP" ? ProxyProtocol.Http : ProxyProtocol.Socks5, p.Host, p.Port, p.User, p.Pass ?? "");
            added.Add(e);
        }
        ImportVisible = false; ImportText = "";
        Reload();
        Toasts.Show($"{added.Count} proxy(s) importé(s).");
        if (ImportTest && CanUseProxys)
            foreach (var item in Proxies.Where(it => added.Any(a => a.Id == it.Model.Id)).ToList()) await TestAsync(item);
    }
}
