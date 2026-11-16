using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.ViewModels;

/// <summary>Signal d'un deplacement pour l'animation : l'acteur et les cellules de son chemin (depart -> destination).</summary>
public sealed record MoveInfo(long ActorId, IReadOnlyList<int> Cells);

/// <summary>Une ligne de l'onglet Inventaire : icone du jeu (par gid) + nom + quantite (×N) + gid.</summary>
public sealed class InventoryItemRow
{
    public int TooltipPid { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Name { get; }
    public string QtyText { get; }
    public string GidText { get; }
    public int Qty { get; }
    public long Gid { get; }
    /// <summary>typeId (0 si inconnu) : cle stable de la categorie (type d'objet), pour filtrer/grouper.</summary>
    public int CategoryId { get; }
    /// <summary>Nom de la categorie (type d'objet : ex "Bois", "Minerai"), ou "Autres" si non resolu.</summary>
    public string Category { get; }

    /// <summary>Instance de la pile (les actions de l'app visent CETTE pile) et position (63 = sac, sinon équipé).</summary>
    public int Uid { get; }
    public int Position { get; }
    public bool IsEquipped => Position != 63;
    /// <summary>Objet équipable (a au moins un emplacement d'équipement) : sans ça, « Équiper » n'a aucun sens (ressources, consommables…).</summary>
    public bool CanEquip => Stores.Launch.EquipPositions(Gid).Any(p => p is >= 0 and <= 15 or 28);
    /// <summary>« Utiliser » masqué pour les équipements et les ressources (rien à utiliser).</summary>
    public bool CanUse => !CanEquip && !(Category?.Contains("essource", StringComparison.OrdinalIgnoreCase) == true || Category?.Contains("resource", StringComparison.OrdinalIgnoreCase) == true);
    public bool ShowEquip => !IsEquipped && CanEquip;
    public bool ShowUse => !IsEquipped && CanUse;
    /// <summary>Bot propriétaire : cible des commandes du menu clic droit (null = ligne en lecture seule, ex. coffre).</summary>
    public BotRowViewModel? Owner { get; }
    public bool HasActions => Owner is not null;
    /// <summary>Objet jamais donné à la mule (cadenas) : réglage par perso.</summary>
    public bool IsKept { get; }
    /// <summary>Quantité de la case en grille (vide pour 1, comme dans le jeu).</summary>
    public string QtyBadge => Qty <= 1 ? "" : Qty >= 100_000 ? $"{Qty / 1000}k" : Qty.ToString(CultureInfo.InvariantCulture);
    public bool HasQtyBadge => Qty > 1;
    /// <summary>Infobulle texte (repli) : nom, quantité, gid, puis les effets.</summary>
    public string Tip => $"{Name}\n{QtyText} · {GidText}" + (IsEquipped ? "\n" + Loc.T("L.Inv.WornTip") : "")
                         + (HasEffects ? "\n" + string.Join("\n", EffectLines.Select(l => l.Text)) : "");
    /// <summary>Sous-titre de l'infobulle et du panneau de détails : niveau · catégorie · quantité.</summary>
    public string InfoLine { get; }
    /// <summary>Effets de CET exemplaire (jets, forgemagie), comme dans l'infobulle du jeu ; malus en rouge.</summary>
    public IReadOnlyList<ItemEffectLine> EffectLines { get; }
    public bool HasEffects => EffectLines.Count > 0;

    /// <param name="pid">Perso du coffre quand la ligne n'a pas de propriétaire (onglet Banque) : sert aux effets.</param>
    public InventoryItemRow(InventoryStore.Item it, BotRowViewModel? owner = null, uint pid = 0)
    {
        Qty = it.Qty; Gid = it.Gid; Uid = it.Uid; Position = it.Position; Owner = owner;
        IsKept = owner?.IsMuleKept(it.Gid) == true;
        Name = Stores.Launch.ItemDisplayName(it.Gid);
        QtyText = "×" + it.Qty.ToString(CultureInfo.InvariantCulture);
        GidText = "gid " + it.Gid.ToString(CultureInfo.InvariantCulture);
        Icon = Stores.InteractiveIcons.GetByIcon(Stores.Launch.ItemIconId(it.Gid));   // asset du jeu (item_assets), null -> pastille
        CategoryId = Stores.Launch.ItemTypeId(it.Gid);
        Category = Stores.Launch.ItemCategory(it.Gid) ?? Loc.T("L.Bots.CatOther");
        int level = Stores.Launch.ItemLevel((int)it.Gid);
        InfoLine = string.Join(" · ", new[] { level > 0 ? Loc.Fmt("L.Info.LevelN", level) : null, Category, QtyText }.Where(x => x is not null));
        uint p = owner is not null ? (uint)Math.Max(0, owner.Pid) : pid;
        TooltipPid = (int)p;
        EffectLines = p == 0 ? Array.Empty<ItemEffectLine>()
                             : Stores.Launch.ItemEffectLines(p, it.Uid).Select(l => new ItemEffectLine(l.Text, l.Negative)).ToList();
    }
}

/// <summary>Une ligne d'effet d'objet (« 143 Vitalité ») ; `Negative` = malus, affiché en rouge comme dans le jeu.</summary>
public sealed record ItemEffectLine(string Text, bool Negative);

/// <summary>Une ligne de la console : horodatage, sens, code obfusque, nom clair (si reconnu), + l'arbre decode.</summary>
public sealed partial class PacketLineViewModel : ObservableObject
{
    private static readonly IBrush OutBrush = new ImmutableSolidColorBrush(Color.Parse("#7FB2FF")); // sortant client -> serveur
    private static readonly IBrush InBrush = new ImmutableSolidColorBrush(Color.Parse("#8BD5A0"));  // entrant serveur -> client
    private static readonly IBrush DimBrush = new ImmutableSolidColorBrush(Color.Parse("#79828F")); // non reconnu

    /// <summary>Paquet ÉPINGLÉ : survit à l'élagage du flux (les 500 lignes) et se filtre via « épinglés seuls ».</summary>
    [ObservableProperty] public partial bool Pinned { get; set; }

    public string Time { get; }
    public bool Out { get; }
    public string Code { get; }
    public string? Name { get; }
    public string Preview { get; }
    public IReadOnlyList<ProtoField> Payload { get; }
    /// <summary>Octets bruts du GameMessage (non préfixés) tels que capturés — pour la vue hex de l'inspecteur.</summary>
    public byte[] RawFrame { get; }

    public string Arrow => Out ? "↑" : "↓";
    // Le CODE obfusqué n'est montré à côté du nom QUE pour une licence développeur (identifier/corriger les mappings depuis la
    // console). Pour tout le monde : uniquement le nom clair (et les champs) ; un code non identifié -> « (non identifié) ».
    // Un utilisateur normal ne doit jamais retrouver la correspondance code -> nom via la console.
    private static bool Dev => LicenseService.IsDeveloper;
    public string Display => Name is null ? (Dev ? Code : "(non identifié)") : (Dev ? $"{Name}  ·  {Code}" : Name);
    public IBrush Brush => Name is null ? DimBrush : (Out ? OutBrush : InBrush);

    private string? _dump;
    private bool _dumpDev;
    /// <summary>Arbre decode formate (champ par champ, imbrique) - pour inspecter le contenu au runtime. Reconstruit si la
    /// licence change (un texte construit en développeur contient le code).</summary>
    public string Dump { get { bool d = Dev; if (_dump is null || _dumpDev != d) { _dumpDev = d; _dump = BuildDump(); } return _dump; } }

    private string? _json;
    private bool _jsonDev;
    /// <summary>Arbre décodé en JSON valide (liste de {field, type:valeur}), pour copier/analyser dans un outil externe.</summary>
    public string Json { get { bool d = Dev; if (_json is null || _jsonDev != d) { _jsonDev = d; _json = BuildJson(); } return _json; } }

    // Une chaîne « type.ankama.com/<code> » (Any imbriqué) porte le code obfusqué : masquée hors licence développeur.
    private static string Str(string s) => !Dev && s.StartsWith("type.ankama.com/", StringComparison.Ordinal) ? "type.ankama.com/•" : s;

    private string? _hex;
    /// <summary>Dump hexadécimal (offset + octets + ASCII) de la trame brute — licence DÉVELOPPEUR uniquement : la trame contient
    /// le code du message en clair (type.ankama.com/…).</summary>
    public string Hex => Dev ? (_hex ??= BuildHex()) : "";

    private string? _blob;
    /// <summary>Texte indexé (minuscules) pour la recherche : nom, aperçu et arbre décodé. Le code obfusqué n'y est indexé que
    /// pour une licence développeur (sinon on pourrait retrouver un nom clair en cherchant un code brut).</summary>
    private bool _blobDev;
    public string SearchBlob { get { bool d = Dev; if (_blob is null || _blobDev != d) { _blobDev = d; _blob = ($"{(d ? Code : "")} {Name} {Preview} {Dump}").ToLowerInvariant(); } return _blob; } }

    public PacketLineViewModel(string time, bool @out, string code, string? name, string preview, IReadOnlyList<ProtoField> payload, byte[] rawFrame)
    {
        Time = time; Out = @out; Code = code; Name = name; Preview = preview; Payload = payload; RawFrame = rawFrame ?? System.Array.Empty<byte>();
    }

    private string BuildDump()
    {
        var sb = new StringBuilder();
        // Le code obfusqué (entre crochets) n'est montré qu'en licence développeur ; sinon le nom clair seul.
        sb.Append(Name ?? (Dev ? Code : "(non identifié)"));
        if (Dev) sb.Append("  [").Append(Code).Append(']');
        sb.Append("  (").Append(Out ? "sortant" : "entrant").Append(')').AppendLine();
        Format(sb, Payload, 0);
        return sb.ToString().TrimEnd();
    }

    private static void Format(StringBuilder sb, IReadOnlyList<ProtoField> fields, int depth)
    {
        foreach (var f in fields)
        {
            sb.Append(' ', depth * 2).Append('#').Append(f.Field).Append(' ');
            switch (f.Wire)
            {
                case WireType.Varint: sb.Append("int ").Append(f.Varint); break;
                case WireType.I32: sb.Append("i32 ").Append(f.AsI32); break;
                case WireType.I64: sb.Append("i64 ").Append(f.AsI64); break;
                case WireType.Len when f.Message is not null: sb.Append("msg"); break;
                case WireType.Len when f.String is not null: sb.Append("str \"").Append(Str(f.String)).Append('"'); break;
                case WireType.Len: sb.Append("bytes[").Append(f.Bytes?.Length ?? 0).Append(']'); break;
                default: sb.Append('?'); break;
            }
            sb.AppendLine();
            if (f.Message is not null) Format(sb, f.Message, depth + 1);
        }
    }

    // JSON VALIDE : chaque champ = { "field":N, "<type>":valeur }. Une liste (pas un objet) -> préserve les champs
    // RÉPÉTÉS (même numéro) et le TYPE wire, sans clés dupliquées. Les sous-messages sont récursifs (type "msg").
    private string BuildJson() { var sb = new StringBuilder(); WriteJson(sb, Payload); return sb.ToString(); }
    private static void WriteJson(StringBuilder sb, IReadOnlyList<ProtoField> fields)
    {
        sb.Append('[');
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"field\":").Append(f.Field).Append(',');
            switch (f.Wire)
            {
                case WireType.Varint: sb.Append("\"int\":").Append(f.Varint); break;
                case WireType.I32: sb.Append("\"i32\":").Append(f.AsI32); break;
                case WireType.I64: sb.Append("\"i64\":").Append(f.AsI64); break;
                case WireType.Len when f.Message is not null: sb.Append("\"msg\":"); WriteJson(sb, f.Message); break;
                case WireType.Len when f.String is not null: sb.Append("\"str\":\"").Append(JsonEscape(Str(f.String))).Append('"'); break;
                case WireType.Len: sb.Append("\"bytes\":\"").Append(f.Bytes is null ? "" : Convert.ToHexString(f.Bytes)).Append('"'); break;
                default: sb.Append("\"raw\":null"); break;
            }
            sb.Append('}');
        }
        sb.Append(']');
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
            }
        return sb.ToString();
    }

    private string BuildHex()
    {
        var f = RawFrame;
        if (f.Length == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < f.Length; i += 16)
        {
            sb.Append(i.ToString("X4")).Append("  ");
            int n = System.Math.Min(16, f.Length - i);
            for (int j = 0; j < 16; j++)
            {
                sb.Append(j < n ? f[i + j].ToString("X2") + " " : "   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int j = 0; j < n; j++) { byte b = f[i + j]; sb.Append(b >= 32 && b < 127 ? (char)b : '.'); }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Un bot = un compte en jeu (enveloppe l'AccountRow), plus sa console de paquets decodes (avec contenu inspectable).
/// Feed() est appelee depuis les threads du relais ; DrainToUi() recopie la file cote UI (bot selectionne seulement).
/// </summary>
public partial class BotRowViewModel : ObservableObject
{
    private const int MaxLines = 500;

    public AccountRow Row { get; }
    /// <summary>Pid du client. Change seulement par <see cref="Rekey"/> (« Reconnecter » MITM en place : nouveau Dofus.exe).</summary>
    public int Pid { get; private set; }

    /// <summary>Console de diagnostic transport PAR BOT (onglet Console) : lignes [inject]/[combat]/[mp]/[map]... attribuées
    /// à CE pid via le pid ambiant de TransportLog. Filtre le firehose global au bot sélectionné.</summary>
    public ObservableCollection<string> TransportConsole => Services.TransportDiag.LinesFor((uint)Pid);

    public ObservableCollection<PacketLineViewModel> ConsoleLines { get; } = new();   // VUE filtrée (bouton + recherche)
    private readonly List<PacketLineViewModel> _allPackets = new();                   // backing complet (borné à MaxLines)
    private readonly ConcurrentQueue<PacketLineViewModel> _incoming = new();
    private long _total, _recognized;

    /// <summary>Objets de l'inventaire AFFICHÉS (filtrés/triés) pour l'onglet Inventaire ; _invFull = liste complète.</summary>
    public ObservableCollection<InventoryItemRow> InventoryItems { get; } = new();
    private readonly List<InventoryItemRow> _invFull = new();
    public bool HasInventory => _invFull.Count > 0;
    public int InventoryCount => _invFull.Count;
    private long _invSig = -1;
    private bool _invIconsLoaded;

    /// <summary>Les métiers réellement annoncés par le serveur pour ce personnage.</summary>
    public ObservableCollection<JobRow> Jobs { get; } = new();
    public bool HasJobs => Jobs.Count != 0;
    private long _jobsSig = long.MinValue;
    private Dictionary<int, string>? _jobNames;

    /// <summary>Actualise les métiers sans modifier les lignes si leur progression est inchangée.</summary>
    public void RefreshJobs()
    {
        if (Pid <= 0) return;
        var levels = Stores.Launch.JobLevels((uint)Pid);
        var progress = Stores.Launch.JobProgress((uint)Pid);
        var known = levels.Where(x => x.Key > 0 && x.Value > 0).OrderBy(x => x.Key).ToArray();
        long signature = known.Length;
        foreach (var (id, level) in known)
        {
            var p = progress.TryGetValue(id, out var snapshot) && snapshot.Level == level
                ? snapshot : new JobProgressSnapshot(level);
            signature = unchecked(signature * 1000003 + id * 397L + level);
            signature = unchecked(signature * 1000003 + (p.Experience ?? -1));
            signature = unchecked(signature * 1000003 + (p.LevelFloor ?? -1));
            signature = unchecked(signature * 1000003 + (p.NextLevelFloor ?? -1));
        }
        if (_jobsSig == signature) return;
        _jobsSig = signature;

        if (_jobNames is null)
        {
            try
            {
                _jobNames = Stores.Launch.GameData?.Jobs()
                    .Where(j => j.Id > 0 && !string.IsNullOrWhiteSpace(j.Name))
                    .GroupBy(j => j.Id).ToDictionary(g => g.Key, g => g.First().Name)
                    ?? new Dictionary<int, string>();
            }
            catch
            {
                // Sans table JobData lisible, les ids restent affichés sans nom inventé.
                _jobNames = new Dictionary<int, string>();
            }
        }

        Jobs.Clear();
        foreach (var (id, level) in known)
        {
            var jr = new JobRow(id, _jobNames.GetValueOrDefault(id),
                progress.TryGetValue(id, out var snapshot) && snapshot.Level == level
                    ? snapshot : new JobProgressSnapshot(level));
            Jobs.Add(jr);
            int jobIcon = Stores.Launch.GameData?.JobIconIdOf(id) ?? 0;
            _ = JobIcons.RequestAsync(jobIcon, bmp => Avalonia.Threading.Dispatcher.UIThread.Post(() => jr.Icon = bmp));
        }
        OnPropertyChanged(nameof(HasJobs));
    }

    /// <summary>Recherche + tri de l'onglet Inventaire (0=défaut, 1=Nom A→Z, 2=Quantité ↓, 3=Catégorie).</summary>
    [ObservableProperty] public partial string InventoryFilter { get; set; } = "";
    [ObservableProperty] public partial int InventorySortIndex { get; set; }
    /// <summary>Super-categorie selectionnee (ou libelle "Toutes" = pas de filtre) ; alimentee par InventoryCategories.</summary>
    [ObservableProperty] public partial string? InventoryCategory { get; set; }
    /// <summary>Super-categories presentes dans l'inventaire (index 0 = "Toutes"), source du ComboBox de filtre.</summary>
    public ObservableCollection<string> InventoryCategories { get; } = new();
    partial void OnInventoryFilterChanged(string value) => ApplyItemView(_invFull, InventoryItems, InventoryFilter, InventorySortIndex, InventoryCategory);
    partial void OnInventorySortIndexChanged(int value) => ApplyItemView(_invFull, InventoryItems, InventoryFilter, InventorySortIndex, InventoryCategory);
    partial void OnInventoryCategoryChanged(string? value) => ApplyItemView(_invFull, InventoryItems, InventoryFilter, InventorySortIndex, value);

    /// <summary>Kamas du perso (formatés), capté sur le snapshot d'inventaire. "—" tant qu'inconnu.</summary>
    [ObservableProperty] public partial string KamasText { get; set; } = "—";

    internal static string FormatKamas(long k)
        => k >= 1_000_000 ? (k / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + " M"
         : k >= 1_000 ? k.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ")
         : k.ToString(CultureInfo.InvariantCulture);

    /// <summary>Objets du COFFRE AFFICHÉS (filtrés/triés) pour l'onglet Banque ; _bankFull = liste complète.</summary>
    public ObservableCollection<InventoryItemRow> BankItems { get; } = new();
    private readonly List<InventoryItemRow> _bankFull = new();
    public bool HasBank => _bankFull.Count > 0;
    public int BankCount => _bankFull.Count;
    private long _bankSig = -1;
    private bool _bankIconsLoaded;

    /// <summary>Kamas dans le coffre (formatés), capté à l'ouverture de la banque. "—" tant qu'inconnu.</summary>
    [ObservableProperty] public partial string BankKamasText { get; set; } = "—";
    [ObservableProperty] public partial string BankFilter { get; set; } = "";
    [ObservableProperty] public partial int BankSortIndex { get; set; }
    [ObservableProperty] public partial string? BankCategory { get; set; }
    public ObservableCollection<string> BankCategories { get; } = new();
    partial void OnBankFilterChanged(string value) => ApplyItemView(_bankFull, BankItems, BankFilter, BankSortIndex, BankCategory);
    partial void OnBankSortIndexChanged(int value) => ApplyItemView(_bankFull, BankItems, BankFilter, BankSortIndex, BankCategory);
    partial void OnBankCategoryChanged(string? value) => ApplyItemView(_bankFull, BankItems, BankFilter, BankSortIndex, value);

    /// <summary>Libelle de l'option "Toutes les categories" (index 0 des ComboBox de filtre) = pas de filtre.</summary>
    private static string CatAll => Loc.T("L.Bots.CatAll");

    // Applique recherche (nom/gid) + filtre par super-categorie + tri à une liste complète -> collection affichée.
    private void ApplyItemView(List<InventoryItemRow> full, ObservableCollection<InventoryItemRow> target, string filter, int sort, string? category)
    {
        IEnumerable<InventoryItemRow> q = full;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            q = q.Where(r => r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                          || r.Gid.ToString(CultureInfo.InvariantCulture).Contains(f, StringComparison.Ordinal));
        }
        if (!string.IsNullOrEmpty(category) && category != CatAll)
            q = q.Where(r => r.Category == category);
        q = sort switch
        {
            1 => q.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => q.OrderByDescending(r => r.Qty),
            3 => q.OrderBy(r => r.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => q,
        };
        target.Clear();
        foreach (var r in q) target.Add(r);
    }

    // Reconstruit la liste des super-categories (index 0 = "Toutes") UNIQUEMENT si l'ensemble a change. Sinon target.Clear()
    // retirait l'element selectionne du ComboBox -> le binding two-way ecrivait null dans Inventory/BankCategory -> le filtre
    // sautait a "Toutes" a CHAQUE MAJ de contenu (ex. a chaque ramassage), rendant le filtre inutilisable en botting.
    private static void RebuildCategories(List<InventoryItemRow> full, ObservableCollection<string> target)
    {
        var cats = full.Select(r => r.Category).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (target.Count == cats.Count + 1 && target.Count > 0 && target[0] == CatAll)
        {
            bool same = true;
            for (int i = 0; i < cats.Count; i++) if (target[i + 1] != cats[i]) { same = false; break; }
            if (same) return;   // inchange -> ne pas toucher la collection (preserve la selection du ComboBox)
        }
        target.Clear();
        target.Add(CatAll);
        foreach (var c in cats) target.Add(c);
    }

    // Les lignes portent le texte des effets : elles sont refaites quand les effets changent (forgemagie) et quand les
    // données du client finissent de charger (gabarits « #1 Vitalité » indisponibles avant).
    private long ItemTextSig() => unchecked(Stores.Launch.ItemEffectsStamp((uint)Pid) * 31L + (Stores.Launch.GameNames.IsReady ? 7 : 0));

    /// <summary>Reconstruit la liste du coffre depuis le store si elle a changé (même patron que l'inventaire).</summary>
    public void RefreshBank()
    {
        if (Pid <= 0) return;
        long bk = Stores.Launch.BankKamas((uint)Pid);
        BankKamasText = bk < 0 ? "—" : FormatKamas(bk);
        if (Stores.InteractiveIcons.IsLoaded && !_bankIconsLoaded) { _bankIconsLoaded = true; _bankSig = -1; }
        var items = Stores.Launch.Bank.Items((uint)Pid);
        long sig = items.Count + ItemTextSig();
        foreach (var it in items) sig = unchecked(sig * 1000003 + it.Gid * 97 + it.Qty);
        if (sig == _bankSig) return;
        _bankSig = sig;
        _bankFull.Clear();
        foreach (var it in items) _bankFull.Add(new InventoryItemRow(it, null, (uint)Pid));
        var keepBankCat = BankCategory;
        RebuildCategories(_bankFull, BankCategories);
        BankCategory = keepBankCat is not null && BankCategories.Contains(keepBankCat) ? keepBankCat : CatAll;   // preserve le filtre choisi
        ApplyItemView(_bankFull, BankItems, BankFilter, BankSortIndex, BankCategory);
        OnPropertyChanged(nameof(HasBank));
        OnPropertyChanged(nameof(BankCount));
    }

    /// <summary>Reconstruit la liste d'inventaire depuis le store si elle a changé (signature gid+qty) -> pas de flicker.</summary>
    public void RefreshInventory()
    {
        if (Pid <= 0) return;
        long kamas = Stores.Launch.Kamas((uint)Pid);
        KamasText = kamas < 0 ? "—" : FormatKamas(kamas);
        // Le bundle d'icones charge en tache de fond : quand il devient dispo, forcer un rebuild (sinon icones null figees).
        if (Stores.InteractiveIcons.IsLoaded && !_invIconsLoaded) { _invIconsLoaded = true; _invSig = -1; }
        var items = Stores.Launch.Inventory.Items((uint)Pid);
        if (HdvCornerOpen) RefreshHdvCorner();   // coin HDV de la carte : suit les prix qui arrivent même si le sac ne change pas
        RefreshItemContext();   // menu clic droit : banque / échange / boutique ouverts ?
        long sig = items.Count + ItemTextSig();
        foreach (var it in items) sig = unchecked(sig * 1000003 + it.Gid * 97 + it.Qty + it.Position * 7919L + it.Uid);
        if (sig == _invSig) return;
        _invSig = sig;
        _invFull.Clear();
        foreach (var it in items) _invFull.Add(new InventoryItemRow(it, this));
        RefreshEquipment(items);
        RefreshDetailItem();
        var keepInvCat = InventoryCategory;
        RebuildCategories(_invFull, InventoryCategories);
        InventoryCategory = keepInvCat is not null && InventoryCategories.Contains(keepInvCat) ? keepInvCat : CatAll;   // preserve le filtre choisi
        ApplyItemView(_invFull, InventoryItems, InventoryFilter, InventorySortIndex, InventoryCategory);
        OnPropertyChanged(nameof(HasInventory));
        OnPropertyChanged(nameof(InventoryCount));
    }

    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial string PacketSummary { get; set; } = "0 / 0";
    [ObservableProperty] public partial string Filter { get; set; } = "all"; // all | in | out | named
    // Filtre actif -> surligne le bouton correspondant (Classes.accent), comme les autres sélecteurs par-boutons de l'app.
    public bool FilterIsAll => Filter == "all";
    public bool FilterIsIn => Filter == "in";
    public bool FilterIsOut => Filter == "out";
    public bool FilterIsNamed => Filter == "named";
    partial void OnFilterChanged(string value)
    { OnPropertyChanged(nameof(FilterIsAll)); OnPropertyChanged(nameof(FilterIsIn)); OnPropertyChanged(nameof(FilterIsOut)); OnPropertyChanged(nameof(FilterIsNamed)); RebuildPackets(); }

    /// <summary>Recherche libre dans le flux de paquets : nom, code, aperçu ET valeurs de l'arbre décodé (ex "191104002").</summary>
    [ObservableProperty] public partial string PacketSearch { get; set; } = "";
    partial void OnPacketSearchChanged(string value) => RebuildPackets();

    /// <summary>N'afficher que les paquets ÉPINGLÉS (bascule de la barre d'outils).</summary>
    [ObservableProperty] public partial bool PacketPinnedOnly { get; set; }
    partial void OnPacketPinnedOnlyChanged(bool value) => RebuildPackets();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorText))]
    public partial PacketLineViewModel? SelectedLine { get; set; }

    /// <summary>Vue de l'inspecteur : 0 = arbre décodé, 1 = JSON, 2 = hex brut.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorText), nameof(PacketViewIsTree), nameof(PacketViewIsJson), nameof(PacketViewIsHex))]
    public partial int PacketView { get; set; }
    public bool PacketViewIsTree => PacketView == 0;
    public bool PacketViewIsJson => PacketView == 1;
    public bool PacketViewIsHex => PacketView == 2;
    /// <summary>Texte affiché par l'inspecteur selon la vue choisie (arbre / JSON / hex) pour le paquet sélectionné.</summary>
    public string InspectorText => SelectedLine is null ? "" : PacketView switch { 1 => SelectedLine.Json, 2 when LicenseService.IsDeveloper => SelectedLine.Hex, _ => SelectedLine.Dump };

    /// <summary>Acteurs de la carte courante (reassigne en bloc a chaque MapComplementaryInformationEvent) -> rendu.</summary>
    [ObservableProperty] public partial IReadOnlyList<MapActor>? MapActors { get; set; }

    /// <summary>Couche d'inspection de la carte (filtres, recherche, sélection stable, fiche) : alimentée à chaque changement ci-dessous.</summary>
    public MapInspectorViewModel Inspector { get; } = new();
    private void WireInspector()
    {
        Inspector.XpLookup = gid => Stores.Launch.GameData?.MonsterGradeXp(gid);
        Inspector.NpcActionsLookup = id => Stores.Launch.GameData?.NpcActions(id);
        Inspector.ReplyTextLookup = (npc, reply) => Stores.Launch.GameData?.NpcReplyText(npc, reply);
        Inspector.NpcNameLookup = id => Stores.Launch.GameData?.NpcName(id);
        Inspector.GameDataAvailable = () => Stores.Launch.GameData is not null;
        Inspector.JoinHandler = cell => RequestJoin(cell);
        Inspector.NpcActionHandler = (npc, actionId) => Stores.Launch.NpcActionAsync((uint)Pid, npc.ActorId, actionId);
        Inspector.ReplyHandler = replyId => Stores.Launch.NpcReplyById((uint)Pid, replyId);
        Inspector.LeaveHandler = () => Stores.Launch.LeaveDialog((uint)Pid);
        Inspector.TradeHandler = actorId => Interaction.RequestTrade(actorId);   // fiche joueur -> « Échanger »
        Inspector.OpenInteractionProbe = () => Stores.Launch.OpenInteraction((uint)Pid);
        Inspector.InteractiveTypeNameLookup = id => Stores.Launch.GameData?.InteractiveTypeName(id);
        Inspector.DialogProbe = () => Stores.Launch.NpcDialog((uint)Pid);
        Inspector.DialogText = (msg, pars) => Stores.Launch.GameNames.NpcMessageText(msg, pars) ?? Stores.Launch.GameData?.NpcQuestionText(msg, pars);
        Inspector.ServiceOpenProbe = () => Stores.Launch.HdvAnyOpen((uint)Pid);
        Inspector.SelfInfo = SelfKnownLines;
        Inspector.SelfCellProvider = () => SelfCellId;
        Inspector.MapIdProvider = () => MapId;
        Inspector.AdjacentProvider = cell => SelfCellId is int me && (me == cell || MapPathfinder.CellDistance(me, cell) <= 1);
        Inspector.RecipesOfSkill = skill => Stores.Launch.GameData?.RecipesOfSkill(skill) ?? Array.Empty<GivreClone.App.Services.LotM.RecipeInfo>();
        Inspector.RecipeOf = gid => Stores.Launch.GameData?.Recipe(gid);
        Inspector.InventoryCount = gid => Stores.Launch.Inventory.CountByGid((uint)Pid, gid);
        Inspector.CraftHandler = async (it, skill, recipe, count) =>
        {
            var o = await Stores.Launch.CraftAsync((uint)Pid, it.ElementId, skill.SkillInstanceUid, recipe, count).ConfigureAwait(false);
            return (o.Ok, o.Qty);
        };
        Inspector.HarvestHandler = it => LicenseService.HasPermission(LicenseService.Perm.Gather) && RequestHarvest(it);
        Inspector.FightProbe = () => Stores.Launch.IsInFight((uint)Pid);
        Inspector.PodsFullProbe = () => PodsMax > 0 && PodsCur >= PodsMax * 0.97;   // pods quasi pleins : on s'arrête avant de ne plus pouvoir ramasser
        Inspector.AttackHandler = RequestAttack;   // garde-fous du moteur (position, chemin, fenêtre) ; jamais sans confirmation
    }
    private void PushInspector() => Inspector.Update(MapActors, MapInteractives, Row.CharacterName, MapEpoch);
    partial void OnMapActorsChanged(IReadOnlyList<MapActor>? value) => PushInspector();
    partial void OnMapInteractivesChanged(IReadOnlyList<MapInteractive>? value) { PushInspector(); RefreshTravelShortcuts(); }
    partial void OnMapEpochChanged(int value) => PushInspector();

    /// <summary>Cellules (mov/los/floor) de la carte courante, chargees du bundle client -> rendu relief.</summary>
    [ObservableProperty] public partial IReadOnlyList<MapCell>? MapCells { get; set; }

    /// <summary>Elements interactifs (ressources/zaaps/...) de la carte courante -> icones + clic droit pour recolter.</summary>
    [ObservableProperty] public partial IReadOnlyList<MapInteractive>? MapInteractives { get; set; }

    /// <summary>Pods (poids) actuels du perso : "606 / 1310". Alimente par InventoryWeightEvent (iun). Null tant qu'inconnu.</summary>
    [ObservableProperty] public partial string? Pods { get; set; }

    /// <summary>Pods NUMÉRIQUES (fix 2026-09-03) : pour la BARRE de pods colorée à seuil (onglet Infos). Même source (iun).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodsPercent), nameof(HasPods), nameof(PodsBrush), nameof(PodsOver85))]
    public partial int PodsCur { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodsPercent), nameof(HasPods), nameof(PodsBrush), nameof(PodsOver85))]
    public partial int PodsMax { get; set; }
    public bool HasPods => PodsMax > 0;
    public int PodsPercent => PodsMax > 0 ? (int)System.Math.Min(100, 100L * PodsCur / PodsMax) : 0;
    public bool PodsOver85 => PodsMax > 0 && PodsPercent > 85;
    /// <summary>Couleur de la barre de pods par SEUIL : vert &lt;70 %, ambre 70-90 %, rouge ≥90 % (proche du bank()).</summary>
    public Avalonia.Media.IBrush PodsBrush => PodsPercent >= 90 ? _podsRed : PodsPercent >= 70 ? _podsAmber : _podsGreen;
    private static readonly Avalonia.Media.IBrush _podsGreen = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3FB950"));
    private static readonly Avalonia.Media.IBrush _podsAmber = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D29922"));
    private static readonly Avalonia.Media.IBrush _podsRed   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F85149"));

    /// <summary>Stats de combat du perso (onglet Infos) : PV courants/max, PA, PM. Alimentees par LaunchService.SelfStats
    /// (sync jxg / kub hors combat / pertes de PV). 0 tant qu'inconnues.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LifeText), nameof(HasCharStats))]
    public partial int CharLife { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LifeText), nameof(HasCharStats))]
    public partial int CharMaxLife { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaText), nameof(HasCharStats))]
    public partial int CharPa { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaText))]
    public partial int CharMaxPa { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PmText), nameof(HasCharStats))]
    public partial int CharPm { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PmText))]
    public partial int CharMaxPm { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    public partial int CharRange { get; set; }   // Portée (caractéristique #19) : bonus de PO des sorts boostables

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LifeText))]
    public partial bool LifeIsEstimated { get; set; }

    public string LifeText => CharMaxLife > 0 ? $"{CharLife} / {CharMaxLife}{(LifeIsEstimated ? " (estimés)" : "")}" : "—";
    public string PaText => CharMaxPa > 0 ? $"{CharPa} / {CharMaxPa}" : "—";
    public string PmText => CharMaxPm > 0 ? $"{CharPm} / {CharMaxPm}" : "—";
    public string RangeText => HasCharStats ? CharRange.ToString() : "—";
    public bool HasCharStats => CharMaxLife > 0 || CharMaxPa > 0 || CharMaxPm > 0;

    /// <summary>Dernier deplacement recu (acteur + chemin) -> declenche l'animation le long du chemin.</summary>
    [ObservableProperty] public partial MoveInfo? LastMove { get; set; }

    /// <summary>Incremente a chaque nouvelle CARTE -> la carte repositionne les acteurs sans animation.</summary>
    [ObservableProperty] public partial int MapEpoch { get; set; }

    /// <summary>Id de la carte courante (MapComplementaryInformationEvent) -> requis pour forger un deplacement.</summary>
    [ObservableProperty] public partial long MapId { get; set; }

    // ===== ÉQUIPE (multi-comptes) : appartenance + rôle + présence, pilotés par BotsViewModel (cross-perso). =====
    /// <summary>Id de l'équipe de ce bot (miroir de Team.Members ; null = aucune équipe). SOURCE du badge « en groupe ».</summary>
    [ObservableProperty] public partial string? TeamId { get; set; }
    [ObservableProperty] public partial TeamRoleKind TeamRole { get; set; }
    [ObservableProperty] public partial TeamPresenceKind TeamPresence { get; set; }
    /// <summary>Couleur de l'équipe (poussée par la Team) -> pastille de démarcation sur « Bots en jeu ». Null hors équipe.</summary>
    [ObservableProperty] public partial IBrush? TeamColorBrush { get; set; }
    /// <summary>FOLLOWER d'une équipe FORMÉE : le chef administre/pilote -> on verrouille l'automatisation solo (script Lua,
    /// planning) de ce bot. Seul le profil de Combat (par-perso) et les vues info restent accessibles. Piloté par BotsViewModel.</summary>
    [ObservableProperty] public partial bool FollowerLocked { get; set; }
    public bool IsLeader => TeamRole == TeamRoleKind.Leader;
    public bool IsFollower => TeamRole == TeamRoleKind.Follower;
    /// <summary>Ce bot fait partie d'une équipe -> badge « en groupe » dans la liste des bots en jeu.</summary>
    public bool InTeam => TeamId is not null;
    /// <summary>Badge « Chef » dans l'ARBRE: seulement pour un chef d'une équipe FORMÉE (groupée). Le rôle est
    /// matérialisé même équipe non formée (pour l'édition), mais un bot affiché SOLO ne doit pas porter le badge Chef.</summary>
    public bool ShowLeaderBadge => InTeam && IsLeader;
    public string TeamRoleText => TeamRole switch
    {
        TeamRoleKind.Leader => Loc.T("L.Bots.Team.RoleLeader"),
        TeamRoleKind.Follower => Loc.T("L.Bots.Team.RoleFollower"),
        _ => "",
    };
    partial void OnTeamIdChanged(string? value) { OnPropertyChanged(nameof(InTeam)); OnPropertyChanged(nameof(ShowLeaderBadge)); }
    partial void OnTeamRoleChanged(TeamRoleKind value)
    { OnPropertyChanged(nameof(IsLeader)); OnPropertyChanged(nameof(IsFollower)); OnPropertyChanged(nameof(TeamRoleText)); OnPropertyChanged(nameof(ShowLeaderBadge)); }
    partial void OnTeamPresenceChanged(TeamPresenceKind value)
    { OnPropertyChanged(nameof(PresenceBrush)); OnPropertyChanged(nameof(PresenceText)); }

    private static readonly IBrush PresGreen = new ImmutableSolidColorBrush(Color.Parse("#3BD17E"));
    private static readonly IBrush PresAmber = new ImmutableSolidColorBrush(Color.Parse("#E6B24C"));
    private static readonly IBrush PresRed = new ImmutableSolidColorBrush(Color.Parse("#F0533B"));
    private static readonly IBrush PresGrey = new ImmutableSolidColorBrush(Color.Parse("#6B7280"));
    /// <summary>Couleur de la pastille de présence (couleur SÉMANTIQUE, pas un accent -> brush fixe OK).</summary>
    public IBrush PresenceBrush => TeamPresence switch
    {
        TeamPresenceKind.SameMap or TeamPresenceKind.Connected => PresGreen,   // en jeu/même carte = vert (en ligne, prêt)
        TeamPresenceKind.Elsewhere or TeamPresenceKind.Traveling => PresAmber,
        TeamPresenceKind.InFight => PresRed,
        _ => PresGrey,   // Offline / OtherServer
    };
    public string PresenceText => TeamPresence == TeamPresenceKind.OtherServer && Row.Server is { Length: > 0 } srv && srv != "-"
        ? Loc.Fmt("L.Bots.Team.PresOtherServerName", srv)
        : Loc.T(TeamPresence switch
        {
            TeamPresenceKind.SameMap => "L.Bots.Team.PresSameMap",
            TeamPresenceKind.Elsewhere => "L.Bots.Team.PresElsewhere",
            TeamPresenceKind.Traveling => "L.Bots.Team.PresTraveling",
            TeamPresenceKind.InFight => "L.Bots.Team.PresInFight",
            TeamPresenceKind.Offline => "L.Bots.Team.PresOffline",
            TeamPresenceKind.OtherServer => "L.Bots.Team.PresOtherServer",
            _ => "L.Bots.Team.PresConnected",
        });

    /// <summary>Cellule REELLE du perso (self) = SOURCE du pathfinding. Recalee par prediction (jrw sortant), par le
    /// jsj du serveur, par le refus (jqt) et au chargement de carte. DECOUPLEE du marqueur (qui s'anime via le jsj)
    /// pour eviter le glitch au depart. Null tant que le perso n'est pas localise.</summary>
    [ObservableProperty] public partial int? SelfCellId { get; set; }

    /// <summary>ActorId du perso (self) : characterId authentifié d'abord, sinon acteur Joueur portant le nom du perso. Sert à
    /// reconnaître SON jsj (SelfCellId à jour même sur un déplacement non prédit) et au marqueur jaune de la carte.</summary>
    [ObservableProperty] public partial long? SelfActorId { get; set; }

    /// <summary>Sorties de la carte courante (voisins par direction), lues du client -> boutons de changement de carte.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoTop), nameof(CanGoBottom), nameof(CanGoLeft), nameof(CanGoRight))]
    public partial MapExits? Exits { get; set; }

    public bool CanGoTop => Exits?.HasNeighbour(MapDir.Top) ?? false;
    public bool CanGoBottom => Exits?.HasNeighbour(MapDir.Bottom) ?? false;
    public bool CanGoLeft => Exits?.HasNeighbour(MapDir.Left) ?? false;
    public bool CanGoRight => Exits?.HasNeighbour(MapDir.Right) ?? false;

    public BotRowViewModel(AccountRow row)
    {
        Row = row; Pid = row.Pid ?? 0; WireInspector();
        Interaction = new InteractionWindowViewModel(() => Pid) { ActorsProvider = () => MapActors };
        // « Mode discret » : abonnement FAIBLE (l'événement statique ne retient pas un bot retiré de la liste).
        var weak = new WeakReference<BotRowViewModel>(this);
        Action? privacy = null;
        privacy = () =>
        {
            if (weak.TryGetTarget(out var bot)) bot.OnPropertyChanged(nameof(DisplayAccount));
            else AppConfig.UiPreferencesChanged -= privacy;
        };
        AppConfig.UiPreferencesChanged += privacy;
    }

    /// <summary>Identifiant du compte pour l'affichage, masqué en « Mode discret » (StreamerPrivacy). Row.Login reste brut.</summary>
    public string DisplayAccount => PrivacyMask.MaskLogin(Row.Login, AppConfig.Current.StreamerPrivacy);

    /// <summary>Fenêtre d'interaction (banque / échange / boutique) affichée sur la carte quand un échange est ouvert.</summary>
    public InteractionWindowViewModel Interaction { get; }

    /// <summary>« Reconnecter » MITM en place : la MÊME entrée (position, sélection, historique des paquets, console) passe sur
    /// le pid du nouveau client ; la vue de SESSION (carte, perso localisé, pods, stats, inventaire) repart de zéro et se
    /// repeuple avec la nouvelle session. Thread UI.</summary>
    internal void Rekey(int pid)
    {
        Pid = pid;   // d'abord : une notification qui lèverait plus bas ne laisse jamais l'entrée sur l'ancien pid
        MapActors = null; MapCells = null; MapInteractives = null; Exits = null; LastMove = null;
        MapId = 0; MapEpoch++;
        SelfCellId = null; SelfActorId = null;
        Pods = null; PodsCur = 0; PodsMax = 0;
        CharLife = 0; CharMaxLife = 0; CharPa = 0; CharMaxPa = 0; CharPm = 0; CharMaxPm = 0; CharRange = 0;
        LifeIsEstimated = false;
        _invSig = -1; _bankSig = -1; _jobsSig = long.MinValue;
        Jobs.Clear(); OnPropertyChanged(nameof(HasJobs));
        KamasText = "—"; BankKamasText = "—";
        ResetInfos();
        OnPropertyChanged(nameof(Pid));
        OnPropertyChanged(nameof(TransportConsole));
    }

    /// <summary>Changement de carte vers le bord d'une direction : marche jusqu'a la cellule de transition (world-graph)
    /// puis jqk. Part de la cellule REELLE du perso (SelfCellId) avec la marchabilite de la carte. No-op si pas pret.</summary>
    public void RequestMapChange(MapDir dir)
    {
        if (Pid <= 0 || MapId == 0 || MapCells is null || SelfCellId is not int src)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[map] changement {dir} impossible : pid={Pid} map={MapId} " +
                $"cellules={(MapCells is null ? "non chargees" : "ok")} celluleSelf={(SelfCellId?.ToString() ?? "INCONNUE (bouge le perso une fois)")}");
            return;
        }
        var walk = new bool[MapPathfinder.CellCount];
        foreach (var c in MapCells) if ((uint)c.CellId < walk.Length) walk[c.CellId] = c.Mov;
        Stores.Launch.ChangeMapDirection((uint)Pid, MapId, src, dir, cell => (uint)cell < walk.Length && walk[cell]);
    }

    /// <summary>Clic-pour-deplacer : forge+injecte un MapMovementRequest de source vers target (A* sur cellules mov).
    /// Retourne true si le paquet est parti (chemin trouve + injecte).</summary>
    /// <summary>« Rejoindre » : marche jusqu'à une case marchable ADJACENTE à la cellule visée (PNJ / bâtiment) ; false si la position est inconnue
    /// ou qu'aucune case adjacente n'est atteignable. N'envoie aucune action d'ouverture.</summary>
    public bool RequestJoin(int targetCell)
    {
        if (Pid <= 0 || MapId == 0 || MapCells is null || SelfCellId is not int src) return false;
        var walk = new bool[MapPathfinder.CellCount];
        foreach (var c in MapCells) if ((uint)c.CellId < walk.Length) walk[c.CellId] = c.Mov;
        if (src == targetCell || MapPathfinder.CellDistance(src, targetCell) <= 1) return true;   // déjà au contact
        foreach (var c in MapPathfinder.StandCellsInRange(targetCell, 1, src, cell => (uint)cell < walk.Length && walk[cell]))
            if (MapPathfinder.KeyMovements(src, c, cell => (uint)cell < walk.Length && walk[cell], allowDiag: true, null).Count > 0)
                return Stores.Launch.MoveTo((uint)Pid, MapId, src, c, cell => (uint)cell < walk.Length && walk[cell]);
        return false;
    }

    /// <summary>Lignes RÉELLEMENT connues du perso contrôlé : une valeur inconnue (0 / « — ») n'est jamais affichée.</summary>
    private IReadOnlyList<InspectorLine> SelfKnownLines()
    {
        var l = new List<InspectorLine>();
        if (CharMaxLife > 0) l.Add(new(Loc.T("L.MapInsp.Life"), LifeText));
        if (CharMaxPa > 0) l.Add(new(Loc.T("L.MapInsp.Pa"), PaText));
        if (CharMaxPm > 0) l.Add(new(Loc.T("L.MapInsp.Pm"), PmText));
        if (!string.IsNullOrWhiteSpace(Pods)) l.Add(new(Loc.T("L.MapInsp.Pods"), Pods!));
        if (KamasText is { Length: > 0 } k && k != "—") l.Add(new(Loc.T("L.MapInsp.Kamas"), k));
        foreach (var j in Jobs) l.Add(new(j.Name, Loc.Fmt("L.MapInsp.JobLevel", j.Level)));
        return l;
    }

    public bool RequestMove(int source, int target)
    {
        if (Pid <= 0 || MapId == 0 || MapCells is null) return false;
        var walk = new bool[MapPathfinder.CellCount];
        foreach (var c in MapCells) if ((uint)c.CellId < walk.Length) walk[c.CellId] = c.Mov;
        return Stores.Launch.MoveTo((uint)Pid, MapId, source, target, cell => (uint)cell < walk.Length && walk[cell]);
    }

    /// <summary>Clic-droit-pour-recolter : marche vers une cellule adjacente a l'interactif puis envoie le
    /// InteractiveUseRequest (iwo) a l'arrivee. Part de la cellule REELLE du perso (SelfCellId). No-op si pas pret.</summary>
    public bool RequestHarvest(MapInteractive interactive)
    {
        if (Pid <= 0 || MapId == 0 || MapCells is null || SelfCellId is not int src)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[recolte] impossible : pid={Pid} map={MapId} " +
                $"cellules={(MapCells is null ? "non chargees" : "ok")} celluleSelf={(SelfCellId?.ToString() ?? "INCONNUE (bouge le perso une fois)")}");
            return false;
        }
        var walk = new bool[MapPathfinder.CellCount];
        foreach (var c in MapCells) if ((uint)c.CellId < walk.Length) walk[c.CellId] = c.Mov;
        return Stores.Launch.Harvest((uint)Pid, MapId, src, interactive, cell => (uint)cell < walk.Length && walk[cell]);
    }

    /// <summary>Clic-droit "Attaquer" sur un groupe de monstres : marche jusqu'a une case adjacente au groupe puis
    /// envoie AttackMonsterRequest (hqa) avec l'id du groupe (l'actor id, negatif). Part de la cellule REELLE (SelfCellId).</summary>
    public bool RequestAttack(MapActor monster)
    {
        if (Pid <= 0 || MapId == 0 || monster is null) return false;
        uint pid = (uint)Pid;
        long mapId = MapId;
        var world = Stores.World.Get(pid);
        var lease = MonsterAttackTargetLease.Capture(world, mapId, monster);
        if (lease is null || !world.CellsReadyFor(mapId) || world.SelfCell is not int src) return false;
        return Stores.Launch.AttackMonster(pid, mapId, src, monster.ActorId, monster.CellId,
            world.IsWalkable, monster.Name, world.StepAllowed, () => lease.IsCurrent(world),
            cell => lease.IsAtConfirmedArrival(world, cell),
            (cell, submit) => lease.TrySubmitFromConfirmedCell(world, cell, submit));
    }

    // ---- threads du relais ----
    // On capture TOUT (le filtre bouton + la recherche sont appliqués côté VUE, pas à la capture) : changer de filtre
    // ou chercher re-filtre alors les paquets DÉJÀ reçus, au lieu de ne toucher que les suivants.
    public void Feed(DecodedPacket p)
    {
        Interlocked.Increment(ref _total);
        if (p.Name is not null) Interlocked.Increment(ref _recognized);
        // CHAQUE bot capture SES paquets (historique par-bot, indépendant) : le backing _allPackets est toujours alimenté ;
        // seule la VUE observable (ConsoleLines) n'est tenue à jour que pour le bot sélectionné (cf DrainToUi) -> pas de
        // churn UI pour les bots non affichés, mais vider un bot ne vide plus les autres.
        _incoming.Enqueue(new PacketLineViewModel(
            DateTime.Now.ToString("HH:mm:ss"), p.ClientToServer, p.Code, p.Name, Preview(p.Payload), p.Payload, p.RawFrame));
    }

    // Le paquet passe-t-il le filtre bouton (all/in/out/named) + « épinglés seuls » + la recherche (nom/code/aperçu/valeurs) ?
    private bool PassesView(PacketLineViewModel l)
    {
        if (PacketPinnedOnly && !l.Pinned) return false;
        bool okFilter = Filter switch { "in" => !l.Out, "out" => l.Out, "named" => l.Name is not null, _ => true };
        if (!okFilter) return false;
        var q = (PacketSearch ?? "").Trim();
        return q.Length == 0 || l.SearchBlob.Contains(q.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static string Preview(IReadOnlyList<ProtoField> payload) => payload.Count == 0 ? "" : $"{payload.Count} champ(s)";

    // Élague en gardant les ÉPINGLÉS : retire le plus ancien NON épinglé (les épinglés survivent au firehose).
    private static void TrimKeepingPinned(System.Collections.Generic.IList<PacketLineViewModel> list, int max)
    {
        while (list.Count > max)
        {
            int i = 0; while (i < list.Count && list[i].Pinned) i++;
            if (i >= list.Count) break;   // tout est épinglé -> on ne coupe plus
            list.RemoveAt(i);
        }
    }

    // ---- thread UI (timer) ----
    public void DrainToUi()
    {
        bool any = false;
        while (_incoming.TryDequeue(out var line))
        {
            _allPackets.Add(line);
            if (Selected && PassesView(line)) { ConsoleLines.Add(line); any = true; }   // vue live : bot affiché seulement
        }
        TrimKeepingPinned(_allPackets, MaxLines);                 // borne le backing en préservant les épinglés
        if (any) TrimKeepingPinned(ConsoleLines, MaxLines);       // idem pour la vue
    }

    /// <summary>Reconstruit la VUE depuis le backing selon le filtre bouton + la recherche (changement de filtre/recherche).</summary>
    private void RebuildPackets()
    {
        var keepSel = SelectedLine;
        ConsoleLines.Clear();
        var matches = _allPackets.Where(PassesView).TakeLast(150);
        foreach (var l in matches) ConsoleLines.Add(l);
        SelectedLine = keepSel is not null && ConsoleLines.Contains(keepSel) ? keepSel : null;   // garde la sélection si encore visible
    }

    public void RefreshSummary() => PacketSummary = $"{Interlocked.Read(ref _total)} / {Interlocked.Read(ref _recognized)}";

    public void ResetConsole()
    {
        while (_incoming.TryDequeue(out _)) { }
        _allPackets.Clear();
        ConsoleLines.Clear();
        SelectedLine = null;
    }

    /// <summary>Reconstruit la VUE paquets depuis le backing du bot (appelé quand il DEVIENT sélectionné) : on retrouve
    /// son propre historique récent au lieu d'une console vide.</summary>
    public void SyncPacketView() => RebuildPackets();
}
