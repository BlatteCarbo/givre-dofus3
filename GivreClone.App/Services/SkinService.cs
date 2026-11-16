using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using GivreClone.App.Skins;

namespace GivreClone.App.Services;

// Skin « »: recolore TOUTE l'app (gris/argent + accent violet, contrôles plats) en repeignant
// la palette de base + en surchargeant les styles de contrôles. Appliqué UNE FOIS au démarrage (App.axaml.cs),
// AVANT la construction de la fenêtre, pour que toutes les résolutions {StaticResource} des vues tombent sur
// les valeurs un bot tiers. Chemin par défaut = strict no-op -> revert garanti (rien n'est ajouté).
//
// Pourquoi au démarrage (et pas à chaud) : la palette de base est consommée en {StaticResource} (~554 refs),
// figée au chargement de chaque vue. Un swap à chaud ne repeindrait que les pages reconstruites, laissant le
// chrome de fenêtre et les overlays (construits une fois) mi-skinnés. Un changement de skin demande donc un
// redémarrage (l'app tourne en AOT/Release et redémarre proprement).
public static class SkinService
{
    /// <summary>Interrupteur GLOBAL de la skin un bot tiers. true = option disponible dans les Réglages (le DÉFAUT reste l'UI
    /// GivreClone tant que l'utilisateur ne bascule pas UiSkin="classic" ; le swap est basé sur un redémarrage). false =
    /// fonctionnalité désactivée (option cachée, skin jamais appliquée même si UiSkin="classic" est persisté).</summary>
    public static readonly bool FeatureEnabled = true;

    /// <summary>La skin un bot tiers est-elle active pour cette session (lue au démarrage) ?</summary>
    public static bool IsSnow { get; private set; }

    public static void Apply(string? uiSkin)
    {
        if (!FeatureEnabled) return;       // fonctionnalité en pause -> UI GivreClone par défaut
        if (uiSkin != "classic") return;   // défaut : ne rien merger/ajouter (revertible)
        var app = Application.Current;
        if (app is null) return;
        IsSnow = true;

        // (1) Palette: copie chaque entrée un bot tiers en entrée PROPRE de Application.Resources. Les entrées propres
        // priment sur TOUS les MergedDictionaries (Theme.axaml + le thème d'accent), quel que soit l'ordre de fusion.
        var pal = new ClassicPalette();
        foreach (var entry in pal)
            app.Resources[entry.Key] = entry.Value;

        // (2) Styles de contrôles : ajoutés APRÈS Controls.axaml -> les styles plus tardifs (même spécificité) gagnent,
        // ce qui ré-émet en DynamicResource les setters de Controls.axaml figés en StaticResource au chargement.
        app.Styles.Add(new GivreClone.App.Skins.ClassicControls());   // par type (XAML compilé, sûr en NativeAOT)
    }
}
