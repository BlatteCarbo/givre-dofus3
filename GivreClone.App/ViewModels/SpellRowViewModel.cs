using System;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Une ligne de la rotation de sorts dans l'onglet Combat. Enveloppe un <see cref="SpellConfig"/> (persiste) et
/// notifie <c>onChanged</c> a chaque edition -> le VM sauvegarde le profil et le repousse au moteur de combat.
/// </summary>
public sealed class SpellRowViewModel : ObservableObject
{
    public SpellConfig Config { get; }
    private readonly Action _onChanged;

    /// <summary>Nom du sort (résolu depuis SpellName du spell_id). Affiché dans la rotation, non éditable. Peut être
    /// re-résolu via <see cref="Refresh"/> quand les tables de noms du jeu finissent de charger (chargement async).</summary>
    public string Name { get; private set; }

    /// <summary>Icône du sort (texture "sort_&lt;iconId&gt;"), ou null si non résolue. Re-résolue par <see cref="Refresh"/>.</summary>
    public Bitmap? Icon { get; private set; }

    public SpellRowViewModel(SpellConfig config, string name, Bitmap? icon, Action onChanged)
    { Config = config; Name = name; Icon = icon; _onChanged = onChanged; }

    /// <summary>Met à jour le nom et l'icône affichés (ex : les bundles du jeu ont fini de charger après la création de
    /// la ligne, où le nom était encore le repli "Sort &lt;id&gt;"). Notifie l'UI seulement si quelque chose change.</summary>
    public void Refresh(string name, Bitmap? icon)
    {
        if (Name != name) { Name = name; OnPropertyChanged(nameof(Name)); }
        if (!ReferenceEquals(Icon, icon)) { Icon = icon; OnPropertyChanged(nameof(Icon)); }
        // La paire et son nom chargent indépendamment du visuel du sort courant.
        // Même un nom/icône inchangé doit réévaluer le bouton initialement masqué.
        OnPropertyChanged(nameof(AltSpellId));
        OnPropertyChanged(nameof(HasVariant));
        OnPropertyChanged(nameof(AltName));
    }

    /// <summary>Lancers max sur une même cible par tour (données du sort, au niveau appris) ; 0 = sans limite ou inconnu.</summary>
    public int PerTarget { get; private set; }
    public bool HasPerTarget => PerTarget > 0;
    public void SetPerTarget(int value)
    {
        value = Math.Max(0, value);
        if (PerTarget == value) return;
        PerTarget = value;
        OnPropertyChanged(nameof(PerTarget)); OnPropertyChanged(nameof(HasPerTarget));
    }

    public int SpellId => Config.SpellId;

    /// <summary>L'autre sort de la paire de variantes (0 = ce sort n'a pas de variante).</summary>
    public int AltSpellId => Stores.Launch.GameNames.SpellVariantPair(SpellId).FirstOrDefault(i => i != SpellId && Unlocked(i));
    /// <summary>Niveau du perso affiché (posé par l'onglet Combat) : une variante pas encore débloquée n'est pas proposée.</summary>
    public static int CharacterLevel { get; set; }
    private static bool Unlocked(int spellId)
        => CharacterLevel <= 0 || Stores.Launch.GameData is not { } db || db.SpellUnlockLevel(spellId) is var lv && (lv <= 0 || lv <= CharacterLevel);
    /// <summary>Après bascule de variante : la ligne porte désormais l'autre sort (mêmes réglages).</summary>
    public void SwitchTo(int spellId, string name, Bitmap? icon)
    {
        Config.SpellId = spellId;
        Refresh(name, icon);
        OnPropertyChanged(nameof(SpellId));
        _onChanged();
    }
    /// <summary>Réglage « × / cible » du joueur (0 = limite du jeu).</summary>
    public int MaxPerTarget
    {
        get => Config.PerTarget;
        set { value = Math.Max(0, value); if (Config.PerTarget != value) { Config.PerTarget = value; OnPropertyChanged(); _onChanged(); } }
    }
    public bool HasVariant => AltSpellId != 0;
    public string AltName => Stores.Launch.GameNames.SpellName(AltSpellId) ?? ("Sort " + AltSpellId);

    public int Priority
    {
        get => Config.Priority;
        set { if (Config.Priority != value) { Config.Priority = value; OnPropertyChanged(); _onChanged(); } }
    }

    /// <summary>Pose la priorite ET rafraichit l'affichage, SANS re-declencher onChanged : le reordonnancement (↑/↓) renumerote
    /// toutes les lignes puis fait UN seul SaveAndPush. Sans cette notif, la colonne Priorite restait figee apres un déplacement.</summary>
    public void SetPriority(int value) { Config.Priority = value; OnPropertyChanged(nameof(Priority)); }

    public int PerTurn
    {
        get => Config.PerTurn;
        set { if (Config.PerTurn != value) { Config.PerTurn = value; OnPropertyChanged(); _onChanged(); } }
    }

    /// <summary>Temps de relance: nombre mini de tours entre 2 lancers de ce sort (0 = aucun). (minCastInterval).</summary>
    public int Cooldown
    {
        get => Config.Cooldown;
        set { if (Config.Cooldown != value) { Config.Cooldown = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCooldown)); _onChanged(); } }
    }
    /// <summary>A une recharge (&gt; 0) -&gt; affiche la pastille « recharge N » dans le résumé de la ligne.</summary>
    public bool HasCooldown => Config.Cooldown > 0;

    public bool Enabled
    {
        get => Config.Enabled;
        set { if (Config.Enabled != value) { Config.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool OnMe
    {
        get => Config.OnMe;
        set { if (Config.OnMe != value) { Config.OnMe = value; OnPropertyChanged(); _onChanged(); } }
    }

    /// <summary>Cibler un ALLIÉ (soin/buff) au lieu d'un ennemi : cible l'allié le plus bas en PV castable d'ici
    /// (combine avec la fenêtre PV cible TgtHpMin/Max pour ne soigner que sous un seuil).</summary>
    public bool TargetAlly
    {
        get => Config.TargetKind == SpellTargetKind.Ally;
        set
        {
            var k = value ? SpellTargetKind.Ally : SpellTargetKind.Enemy;
            if (Config.TargetKind != k) { Config.TargetKind = k; OnPropertyChanged(); OnPropertyChanged(nameof(TargetAdjacent)); _onChanged(); }
        }
    }

    /// <summary>Lancer le sort sur une case ADJACENTE à moi (celle qui touche le plus d'ennemis dans sa zone).</summary>
    public bool TargetAdjacent
    {
        get => Config.TargetKind == SpellTargetKind.Adjacent;
        set
        {
            var k = value ? SpellTargetKind.Adjacent : SpellTargetKind.Enemy;
            if (Config.TargetKind != k) { Config.TargetKind = k; OnPropertyChanged(); OnPropertyChanged(nameof(TargetAlly)); _onChanged(); }
        }
    }

    /// <summary>Sort sur soi de type BUFF : le lancer même si aucun ennemi n'est à portée. Sinon (par défaut), un sort
    /// sur soi OFFENSIF (dégâts en zone) n'est lancé que si un ennemi est dans la zone.</summary>
    public bool SelfBuff
    {
        get => Config.SelfBuff;
        set { if (Config.SelfBuff != value) { Config.SelfBuff = value; OnPropertyChanged(); _onChanged(); } }
    }

    /// <summary>INVOCATION : ce sort place une créature sur une cellule LIBRE à portée (pas de cible ennemie).</summary>
    public bool IsSummon
    {
        get => Config.IsSummon;
        set { if (Config.IsSummon != value) { Config.IsSummon = value; OnPropertyChanged(); _onChanged(); } }
    }
    /// <summary>Nombre MAX d'invocations vivantes de ce sort à moi (plafond ; 0/1 = une seule).</summary>
    public int MaxSummons
    {
        get => Config.MaxSummons;
        set { if (Config.MaxSummons != value) { Config.MaxSummons = value; OnPropertyChanged(); _onChanged(); } }
    }

    // --- Conditions de lancement (0/100 = désactivé) ---
    public int MinAp
    {
        get => Config.MinAp;
        set { if (Config.MinAp != value) { Config.MinAp = value; RaiseCond(); } }
    }
    public int MinTurn
    {
        get => Config.MinTurn;
        set { if (Config.MinTurn != value) { Config.MinTurn = value; RaiseCond(); } }
    }
    public int MinEnemies
    {
        get => Config.MinEnemies;
        set { if (Config.MinEnemies != value) { Config.MinEnemies = value; RaiseCond(); } }
    }
    public int MinAoeHits
    {
        get => Config.MinAoeHits;
        set { if (Config.MinAoeHits != value) { Config.MinAoeHits = value; RaiseCond(); } }
    }
    public int SelfHpMin
    {
        get => Config.SelfHpMin;
        set { if (Config.SelfHpMin != value) { Config.SelfHpMin = value; RaiseCond(); } }
    }
    public int SelfHpMax
    {
        get => Config.SelfHpMax;
        set { if (Config.SelfHpMax != value) { Config.SelfHpMax = value; RaiseCond(); } }
    }
    public int TgtHpMin
    {
        get => Config.TgtHpMin;
        set { if (Config.TgtHpMin != value) { Config.TgtHpMin = value; RaiseCond(); } }
    }
    public int TgtHpMax
    {
        get => Config.TgtHpMax;
        set { if (Config.TgtHpMax != value) { Config.TgtHpMax = value; RaiseCond(); } }
    }

    /// <summary>Au moins une condition n'est PAS à sa valeur neutre (0, ou 0–100 pour les fenêtres PV) -&gt; pastille « conditions actives ».</summary>
    public bool HasActiveConditions =>
        Config.MinAp > 0 || Config.MinTurn > 0 || Config.MinEnemies > 0 || Config.MinAoeHits > 0
        || Config.SelfHpMin > 0 || Config.SelfHpMax < 100
        || Config.TgtHpMin > 0 || Config.TgtHpMax < 100;

    // --- Placement & zones (0 = auto ; AllowApproach vrai par défaut) ---
    /// <summary>Distance MINI de tir imposée (kite par sort) : 0 = portée native du sort.</summary>
    public int CastRangeMin
    {
        get => Config.CastRangeMin;
        set { if (Config.CastRangeMin != value) { Config.CastRangeMin = value; RaisePlace(); } }
    }
    /// <summary>Plafond de portée de tir (se rapprocher au lieu de sniper) : 0 = portée max native.</summary>
    public int CastRangeMax
    {
        get => Config.CastRangeMax;
        set { if (Config.CastRangeMax != value) { Config.CastRangeMax = value; RaisePlace(); } }
    }
    /// <summary>Autoriser le bot à se déplacer pour amener ce sort à portée (false = tir sur place seulement).</summary>
    public bool AllowApproach
    {
        get => Config.AllowApproach;
        set { if (Config.AllowApproach != value) { Config.AllowApproach = value; RaisePlace(); } }
    }

    /// <summary>Le placement de ce sort est personnalisé (portée bornée OU déplacement interdit) -&gt; pastille « placement ».</summary>
    public bool HasPlacement => Config.CastRangeMin > 0 || Config.CastRangeMax > 0 || !Config.AllowApproach;

    private void RaiseCond([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(HasActiveConditions));
        _onChanged();
    }

    private void RaisePlace([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(HasPlacement));
        _onChanged();
    }
}
