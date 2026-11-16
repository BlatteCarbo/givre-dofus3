using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GivreClone.App.ViewModels;

// Types de support de l'onglet Comptes présents dans l'original v1.3.1 et absents du clone avant MZP23.
// Noms de membres = original (docs/MZP23_VIEWMODEL_PARITY.md) ; comportements = choix de ce lot.

/// <summary>Une ligne du résultat d'import de comptes : identifiant, issue, couleur de l'issue. Jamais de mot de passe ici.</summary>
public sealed partial class AccountImportRow : ObservableObject
{
    public string Login { get; init; } = "";
    [ObservableProperty] public partial string Result { get; set; } = "";
    [ObservableProperty] public partial IBrush Brush { get; set; } = Neutral;

    public static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#63D3A6"));
    public static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#F2C14E"));
    public static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#9AA4B2"));
}

/// <summary>Un choix de liste avec libellé et icône facultative (classe, sexe, tête, corps).</summary>
public sealed class PaletteSwatch
{
    public int Rgb { get; init; }
    public Avalonia.Media.IBrush Brush => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb((byte)(Rgb >> 16), (byte)(Rgb >> 8), (byte)Rgb));
}

public sealed class PickOption
{
    public int Value { get; init; }
    public string Label { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
}

/// <summary>Un emplacement de couleur de la création de personnage : valeur 24 bits (ou -1 = non défini) et libellé « #RRGGBB ».</summary>
public sealed partial class CreationColor : ObservableObject
{
    public int Slot { get; init; }
    [ObservableProperty] public partial int Rgb { get; set; } = Services.AccountsLogic.NoColor;
    /// <summary>Couleur par défaut de la classe (RVB) pour cet emplacement, montrée estompée tant que rien n'est choisi ; -1 = inconnue.</summary>
    [ObservableProperty] public partial int DefaultRgb { get; set; } = Services.AccountsLogic.NoColor;
    [ObservableProperty] public partial bool IsActive { get; set; }
    public string Label => Services.AccountsLogic.ColorLabel(Rgb);
    private int Shown => Rgb >= 0 ? Rgb : DefaultRgb;
    public Color Color => Shown < 0 ? Colors.Transparent : Color.FromRgb((byte)(Shown >> 16), (byte)(Shown >> 8), (byte)Shown);
    public Avalonia.Media.IBrush Brush => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color);
    public double ShownOpacity => Rgb >= 0 ? 1.0 : 0.55;
    public string SlotName => Slot switch { 0 => "Peau", 1 => "Cheveux", _ => $"Tenue {Slot - 1}" };
    partial void OnRgbChanged(int value) { OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(Brush)); OnPropertyChanged(nameof(ShownOpacity)); }
    partial void OnDefaultRgbChanged(int value) { OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(Brush)); }
}
