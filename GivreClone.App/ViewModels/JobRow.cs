using Avalonia.Media.Imaging;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Ligne de métier issue d'un événement JobExperience. Les cinq propriétés liées
/// dans l'application originale sont conservées. Aucun taux d'XP n'est inventé.
/// </summary>
public sealed partial class JobRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Id { get; }
    private Bitmap? _icon;
    public Bitmap? Icon { get => _icon; set => SetProperty(ref _icon, value); }
    public string Name { get; }
    public int Level { get; }
    public double Percent { get; }
    public string XpText { get; }
    public bool HasXp { get; }

    public JobRow(int id, string? name, JobProgressSnapshot progress, Bitmap? icon = null)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"Métier #{id}" : name;
        Icon = icon;
        Level = progress.Level;
        Percent = progress.Percent;
        XpText = progress.ExperienceText;
        HasXp = progress.HasExperience;
    }
}
