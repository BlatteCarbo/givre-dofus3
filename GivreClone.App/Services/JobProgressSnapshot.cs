using System;
using System.Globalization;

namespace GivreClone.App.Services;

/// <summary>
/// État d'un métier issu des événements du client. Une montée de niveau peut être
/// connue avant la prochaine mise à jour d'expérience : dans ce cas l'XP est inconnue.
/// </summary>
public sealed record JobProgressSnapshot(int Level, long? Experience = null, long? LevelFloor = null, long? NextLevelFloor = null)
{
    public bool HasExperience => Experience is >= 0 && LevelFloor is >= 0
        && NextLevelFloor > LevelFloor && Experience >= LevelFloor;

    public double Percent => HasExperience
        ? Math.Clamp(100d * ((double)Experience!.Value - LevelFloor!.Value) /
                     ((double)NextLevelFloor!.Value - LevelFloor.Value), 0d, 100d)
        : 0d;

    public string ExperienceText => HasExperience
        ? (Experience!.Value - LevelFloor!.Value).ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))
          + " / " + (NextLevelFloor!.Value - LevelFloor.Value).ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " XP"
        : "XP non disponible";

    /// <summary>Ne reprend pas l'XP d'un ancien niveau lorsque le serveur annonce une montée.</summary>
    public JobProgressSnapshot WithLevelUp(int level) => level == Level ? this : new JobProgressSnapshot(level);
}
