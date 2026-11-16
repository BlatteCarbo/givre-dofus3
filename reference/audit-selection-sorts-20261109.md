# Audit de la Sélection des Sorts par l'IA de Combat (Givre) et Plan de Progression 1 -> 200

**Date :** 9 novembre 2026  
**Cible d'analyse :** Sélection, priorisation et montée en niveau des sorts de combat  
**Cas d'usage prioritaire :** Éliotrope leveling 1 -> 200 (*« quand il va débloquer il faut utiliser les meilleurs sorts »*)  
**Répertoire audité :** `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`  

---

## Synthèse Exécutive

L'audit complet du pipeline de décision du combat (`AutoSpellRotation`, `CombatTurnPlanner`, `SpellResolver`, `GameNameResolver`, `LaunchService.Combat.Turn`, `ClassMechanics`) révèle les causes fondamentales des comportements anormaux constatés en leveling :

1. **L'ordre de la rotation automatique n'est ni figé ni aléatoire :** il est calculé selon une estimation théorique de dégâts par PA décroissants (`Damage(l) / l.Ap`). Cependant, un profil manuel préexistant a priorité absolue, reléguant les sorts nouvellement appris à la fin de la file.
2. **Le déblocage de sort en cours de session est partiellement aveugle :** le bot lit les sorts appris uniquement via le paquet serveur `SpellsEvent` reçu à la connexion (`LaunchService.Packets.cs:1533`). Lors d'un passage de niveau (`CharacterLevelUpEvent`), la liste `_charSpells` n'est pas réévaluée localement, et l'événement `SpellVariantActivationEvent` n'est pas intercepté.
3. **Le filtrage d'élément contient un piège majeur pour les personnages en phase de leveling :** la détection automatique des éléments maîtrisés impose la condition `maxStat >= 80` (`AutoSpellRotation.cs:62`). En dessous de 80 dans sa statistique principale (fréquent aux niveaux 1 à 40 ou en stuff sagesse), le bot considère **tous les éléments comme valides** et lance des sorts hors-élément pour 0 à 10 dégâts au lieu d'optimiser son élément actif.
4. **Les sorts de boost / utilitaires sont quasi-muets pour le planificateur s'ils n'attaquent pas dans le même tour :** un boost pur génère un score immédiat de 0. Il n'est valorisé que par anticipation d'une frappe immédiate dans le même tour (`CombatTurnPlanner.cs:780`). Lancer un boost en préparation d'un tour futur quand l'ennemi est hors de portée n'est pas modélisé.
5. **L'exploration combinatoire (ex: 2 sorts à 2 PA vs 1 sort à 4 PA) fonctionne via le beam search, mais est bridée par `MaxDepth = 3` :** le planificateur peut comparer 2 lancers d'un sort 2 PA contre 1 lancer d'un sort 4 PA, mais ne pourra jamais planifier 4 lancers (ex: 8 PA en sorts 2 PA). De plus, en repli de rotation classique, le beam search n'existe pas et le premier sort disponible de la liste consomme aveuglément les PA.
6. **Les mécaniques Éliotrope sont mortes dans le code de production :** les helpers de portails (`CanHitThroughPortals`, `PortalDamageMultiplier`, `ResolveExitPortal`) déclarés dans `ClassMechanics.cs` ne sont appelés que par leur propre fichier de test unitaire (`ClassMechanicsTests.cs:44,60`). Aucune ligne du planificateur de combat ne consulte les portails en jeu.

---

## 1. Assemblage de la Rotation en Mode Automatique

### Question centrale : Détermination de l'ordre des sorts
En mode automatique (`profile.AutoSpells = true`), la rotation est assemblée dans la méthode `AutoSpellRotation.Build(...)` :
- **Fichier source :** `GivreClone.App/Services/Combat/AutoSpellRotation.cs:45-139`
- **Point d'appel :** `GivreClone.App/Services/LaunchService.Combat.Turn.cs:562-569`

L'ordre des sorts **n'est pas** déterminé par une liste codée en dur, **n'est pas** déterminé par les IDs de sort du jeu, et **n'est pas** déterminé par l'ordre brut renvoyé par le paquet serveur. Il est déterminé par :
1. Les sorts déjà présents dans le profil manuel qui restent prioritaires (`profile.Where(s => s.Enabled && s.SpellId > 0)` à `AutoSpellRotation.cs:51`).
2. Pour les sorts appris découverts non listés dans le profil, l'ordre est dicté par le **ratio dégâts estimés par PA décroissant**, puis par les **dégâts totaux décroissants**, puis par **ID de sort croissant** pour départager.

### Code d'ordonnancement exact (`AutoSpellRotation.cs:117-124`)
```csharp
// GivreClone.App/Services/Combat/AutoSpellRotation.cs:117-124
foreach (var sp in learned
             .Where(l => l.SpellId > 0 && l.SpellId is not (413 or 414) && !listed.Contains(l.SpellId) && l.Ap > 0
                 && (l.Descriptor is { } d ? (options?.RequireDescriptors == true
                         ? CombatTurnPlanner.IsAdmitted(d) : d.CanPlan) && ElementsAllow(d)
                     : options?.RequireDescriptors != true && l.AvgDamage > 0))
             .GroupBy(l => l.SpellId).Select(g => g.First())
             .OrderByDescending(l => Damage(l) / l.Ap).ThenByDescending(Damage).ThenBy(l => l.SpellId))
{
    var targets = sp.Descriptor?.Targets ?? SpellTargets.Enemy;
    bool support = sp.Descriptor is { } desc && desc.Effects.All(e =>
        e.Kind is SpellEffectKind.Heal or SpellEffectKind.Shield
        || e.ActionId is 111 or 112 or 115 or 118 or 119 or 120 or 123 or 126 or 128 or 138 or 150 or 418);
    rotation.Add(new SpellConfig
    {
        SpellId = sp.SpellId, Priority = next, PerTurn = sp.Descriptor is { MaxPerTurn: > 0 } caps ? caps.MaxPerTurn : 99,
        Enabled = true, OnMe = targets == SpellTargets.Self,
        TargetKind = support && (targets & SpellTargets.Ally) != 0 ? SpellTargetKind.Ally : SpellTargetKind.Enemy,
        SelfBuff = support,
    });
    if (next < int.MaxValue) next++;
}
```

### Méthode `Damage(l)` calculant la valeur (`AutoSpellRotation.cs:95-116`)
```csharp
// GivreClone.App/Services/Combat/AutoSpellRotation.cs:95-116
double Damage(LearnedSpellInfo spell)
{
    if (spell.Descriptor is not { } descriptor || options?.Characteristics is not { } characteristics)
        return spell.AvgDamage;

    double dmg = descriptor.Effects.Where(e => e.Kind == SpellEffectKind.Damage
        && !e.TargetMask.Split(',', StringSplitOptions.TrimEntries).All(t => t is "C" or "c" or "a" or "g"))
        .Sum(e => DamageEstimator.Estimate(e.Min, e.Max, DamageEstimator.ElementOf(e, characteristics), characteristics).Average);

    // Bonus pour sorts à faible coût PA (2 PA) : permet un packing optimal (ex: 2x 2 PA pour 4 PA au lieu d'1 sort 3 PA)
    if (spell.Ap == 2) dmg *= 1.25;

    // Bonus pour sorts avec vol/retrait PM ou PA (ex: Flèche d'Immobilisation)
    if (descriptor.Effects.Any(e => e.ActionId is 168 or 169))
        dmg += 20.0;

    // Bonus pour sorts à montée en puissance ou buffs cumulables par coup
    if (descriptor.Effects.Any(e => e.ActionId is 112 or 138 or 2822 or 115))
        dmg += 15.0;

    return dmg;
}
```

### Exécution finale dans le tour (`LaunchService.Combat.Turn.cs:570-575`)
```csharp
// GivreClone.App/Services/LaunchService.Combat.Turn.cs:570-575
var rotation = source
    .Where(sp => sp.Enabled && sp.SpellId > 0)
    .OrderBy(sp => sp.Priority)
    .Select(sp => new RotationSpell(sp, SpellCombatLive(pid, sp.SpellId, LearnedSpellLevel(pid, sp.SpellId))))
    .Where(x => x.Data is not null)
    .ToList();
```

---

## 2. Détection des Sorts Appris, Grades et Cycle de Vie au Leveling

### Source de données et stockage
- **Paquet réseau déclencheur :** `SpellsEvent` (nom protobuf `SpellsEvent`, code obscurci historique `"hms"` sur 3.6.11.12), intercepté dans `GivreClone.App/Services/LaunchService.Packets.cs:1533-1541`.
- **Décodage Protobuf :** `GivreClone.Protocol/CharacterSpellsReader.cs:22-50`.
  - Champ `#1` (`human_spells`), éléments de type `SpellItem` (`fmug` ou champ `#2` = `spell_id`, champ `#4` ou `#5` = `spell_level`).
- **Dictionnaires en mémoire :** `GivreClone.App/Services/LaunchService.Combat.cs:722-723` :
  ```csharp
  private readonly ConcurrentDictionary<uint, IReadOnlyList<int>> _charSpells = new(); // pid -> sorts APPRIS
  private readonly ConcurrentDictionary<uint, IReadOnlyDictionary<int, int>> _charSpellLevels = new(); // pid -> (spell_id -> grade)
  ```

### Accesseur `LearnedSpellLevel` (`LaunchService.Combat.cs:726-728`)
```csharp
// GivreClone.App/Services/LaunchService.Combat.cs:726-728
public int LearnedSpellLevel(uint pid, int spellId) =>
    _charSpellLevels.TryGetValue(pid, out var m) && m.TryGetValue(spellId, out var g) ? g : 1;
```
Et pour la liste des IDs appris (`LaunchService.Combat.cs:1038`) :
```csharp
public IReadOnlyList<int> LearnedSpells(uint pid) =>
    EngineControlled(pid) is { } controlled ? controlled.Fighter.Spells.Keys.ToArray()
    : _charSpells.TryGetValue(pid, out var s) ? s : System.Array.Empty<int>();
```

### Comportement pour un sort non encore débloqué
1. **Dans le profil manuel :** Si un sort de haut niveau (ex: *Convulsion* ou *Affliction* au niv 1) est configuré dans le profil, il est systématiquement filtré à l'entrée du tour (`LaunchService.Combat.Turn.cs:550-552`) :
   ```csharp
   IReadOnlyList<SpellConfig> validProfileSpells = learnedSet is null
       ? profile.Spells
       : profile.Spells.Where(sp => sp.SpellId <= 0 || sp.SpellId == 413 || learnedSet.Contains(sp.SpellId)).ToList();
   ```
2. **Dans le mode automatique :** Seuls les IDs présents dans `learnedIds = LearnedSpells(pid)` sont transmis à `AutoSpellRotation.Build(...)`. Un sort non débloqué n'est jamais injecté.
3. **Dans le planificateur (`CombatTurnPlanner`) :** Tout sort dont l'ID ne figure pas dans `plannerLearnedIds` est exclu avec le motif `"non-appris"` (`LaunchService.Combat.Turn.cs:1092`).

### Comportement au moment précis du déblocage (Level Up)
- **Au sein du combat :** La rotation n'est **pas conservée en cache entre les tours**. `AutoSpellRotation.Build(...)` est réinvoqué au début de **chaque tour de combat** (`LaunchService.Combat.Turn.cs:562`).
- **MAIS la source des sorts appris (`_charSpells`) reste obsolète !**
  - Lors d'un passage de niveau, le serveur envoie `CharacterLevelUpEvent` (`LaunchService.Packets.cs:1565-1571`).
  - Le gestionnaire met à jour `SetCharacterLevel(...)`, mais **ne met pas à jour `_charSpells` ni `_charSpellLevels`**.
  - Si le serveur Dofus n'émet pas spontanément un nouveau `SpellsEvent`, le bot conserve la liste des sorts reçue lors de la connexion.
  - L'événement `SpellVariantActivationEvent` (`{"effective":1, "spell_id":2}` dans `data/fields.json:6301`) qui signale le déblocage et l'activation des variantes n'est **géré nulle part dans le code** (0 occurrence dans tout le dépôt).
  - **Verdict :** Le nouveau sort débloqué reste dormant / invisible pour l'IA jusqu'à ce qu'un événement `SpellsEvent` soit provoqué (reconnexion ou changement de variantes manuel).

---

## 3. Prise en Compte des Caractéristiques Élémentaires du Personnage

### Réponse directe
**Oui**, l'estimation mathématique des dégâts utilise bien les caractéristiques du lanceur... **MAIS un garde-fou mal calibré désactive totalement le filtrage élémentaire pour les personnages de bas niveau**, expliquant pourquoi l'IA lance des sorts hors-élément pour des dégâts dérisoires.

### Formule d'estimation et caractéristiques (`DamageEstimator.cs:34-58`)
```csharp
// GivreClone.App/Services/Combat/DamageEstimator.cs:34-58
public static DamageEstimate Estimate(int min, int max, DamageElement element, IReadOnlyDictionary<int, int> me, DamageTarget? target = null)
{
    int Stat(int id) => me.TryGetValue(id, out var v) ? v : 0;
    double percent = Stat(StatId(element)) + Stat(CharIds.DamagePercent);
    double flat = Stat(CharIds.AllDamageBonus) + Stat(FlatId(element));
    double critChance = Math.Clamp(Stat(CharIds.CriticalHit), 0, 100);
    double critFlat = Stat(CharIds.CriticalDamageBonus);
    double resist = target?.ResistPercent is { } rp && rp.TryGetValue(element, out var r) ? Math.Clamp(r, -100, 100) : 0;
    double reduction = target?.FlatReduction is { } fr && fr.TryGetValue(element, out var f) ? f : 0;

    double One(double roll, bool crit)
    {
        double d = roll * (100 + percent) / 100.0 + flat + (crit ? critFlat : 0);
        d = d * (1 - resist / 100.0) - reduction;
        return Math.Max(0, d);
    }
    // ...
}
```
Ligne clé : `double percent = Stat(StatId(element)) + Stat(CharIds.DamagePercent);` (`DamageEstimator.cs:36`).  
La Force (`CharIds.Strength = 10`), l'Intelligence (`15`), la Chance (`13`) et l'Agilité (`14`) entrent bien dans la formule.

### Pourquoi l'IA lance quand même des sorts hors-élément ?

#### 1. Le seuil destructeur `maxStat >= 80` (`AutoSpellRotation.cs:61-71`)
```csharp
// GivreClone.App/Services/Combat/AutoSpellRotation.cs:61-71
AutoCombatElements detectedElements = AutoCombatElements.Automatic;
if (options is not null && options.Elements == AutoCombatElements.Automatic && maxStat >= 80)
{
    detectedElements = 0;
    int threshold = Math.Max(40, (int)(maxStat * 0.35));
    if (str >= threshold) detectedElements |= AutoCombatElements.Earth | AutoCombatElements.Neutral;
    if (intel >= threshold) detectedElements |= AutoCombatElements.Fire;
    if (cha >= threshold) detectedElements |= AutoCombatElements.Water;
    if (agi >= threshold) detectedElements |= AutoCombatElements.Air;
    if (detectedElements == 0) detectedElements = AutoCombatElements.Automatic;
}
```
- Pour un personnage en cours de leveling (niveaux 1 à 40), `maxStat` est très souvent **inférieur à 80** (ex: 45 en Agilité, 0 dans le reste).
- Conséquence immédiate : la condition `maxStat >= 80` échoue. `detectedElements` reste à `AutoCombatElements.Automatic`.
- À la ligne 79 : `if (effectiveElements == AutoCombatElements.Automatic) return true;`.
- **Résultat :** `ElementsAllow` autorise **100 % des sorts de tous les éléments** ! Les sorts Terre, Feu, Eau, Air entrent tous dans la rotation.

#### 2. Dégâts de base élevés vs faible statistique en début de jeu
Au niveau 15, un sort Feu a une base de 19-21 (moyenne 20). Avec 0 Intelligence, il inflige $20 \times (100+0)/100 = 20$ dégâts.  
Un sort Air a une base de 10-12 (moyenne 11). Avec 40 Agilité, il inflige $11 \times (100+40)/100 = 15.4$ dégâts.  
La fonction de tri `Damage(l)` classe le sort Feu **devant** le sort Air, et le bot le lance alors que le joueur veut monter un Éliotrope Air !

#### 3. PA résiduels et portée dans le planificateur (`CombatTurnPlanner.cs`)
Dans le planificateur, le score est purement additif sur les dégâts (`Score.Damage`). Si l'ennemi est à 5 cases et que le sort principal a une portée de 1 à 3 cases, mais qu'un sort hors-élément a 1 à 6 cases de portée :
- Le sort principal fait 0 (hors de portée).
- Le sort hors-élément inflige 8 dégâts.
- $8 > 0$ : le planificateur choisit de lancer le sort hors-élément.

---

## 4. Évaluation des Sorts de Boost et Utilitaires

### Structure des scores dans le planificateur (`CombatTurnPlanner.cs:70-76`)
```csharp
// GivreClone.App/Services/Combat/CombatTurnPlanner.cs:70-76
public sealed record PlannerScore(double Damage, double Finishes, double Healing, double Shielding,
    double Incoming, double Position, double Uncertainty, double Control = 0, double FriendlyFire = 0)
{
    public double Total => Damage + Finishes + Healing + Shielding + Control - FriendlyFire - Incoming + Position - Uncertainty;
}
```

### Poids attribués aux actions (`CombatTurnPlanner.cs:678-738`)
| Composante | Calcul / Poids | Ligne |
| :--- | :--- | :--- |
| **Dégâts directs** | $1.0 \times \text{dégâts infligés aux ennemis}$ (ou $\times 0.01$ si cible réservée) | `CombatTurnPlanner.cs:715` |
| **Coup de grâce (Kill)** | $+40 + \text{DégâtsAttendusEnnemi} (+10 \text{ si focus team})$ | `CombatTurnPlanner.cs:717` |
| **Soin** | $\text{PVSoignés} \times \text{Survie(PV, MaxPV)} \times \text{priorité (1 ou 2)}$ | `CombatTurnPlanner.cs:734` |
| **Bouclier** | $\min(\text{Bouclier}, \max(0, \text{Incoming} - \text{Protection})) \times 0.5 \times \text{priorité}$ | `CombatTurnPlanner.cs:735` |
| **Invisibilité** | $\min(25.0, \text{Exposition} \times 0.25)$ | `CombatTurnPlanner.cs:702` |
| **Entrave / Retrait PA** | $+8.0 \text{ par PA retiré à un ennemi}, +6.0 \text{ par PA donné à un allié}$ | `CombatTurnPlanner.cs:696` |
| **Entrave / Retrait PM** | $+5.0 \text{ par PM retiré à un ennemi}, +3.0 \text{ par PM donné à un allié}$ | `CombatTurnPlanner.cs:696` |
| **Tir allié (Malus)** | $-4.0 \times \text{Dégâts infligés aux alliés}$ | `CombatTurnPlanner.cs:691` |

### Évaluation des Boosts Purs, Invocations et Portails
Un sort de boost pur (ex: boost de puissance/dommages, portail sans frappe directe) n'inflige aucun dégât, ne soigne pas, ne donne pas de bouclier immédiat et ne retire pas de PA/PM.
Dans `CastOn` : `Damage = 0, Finishes = 0, Healing = 0, Shielding = 0, Control = 0`.  
Son score immédiat `Score(n).Total` est de **0.0** (ou négatif si incertitude de résolution).

Sa seule chance d'être joué réside dans la fonction `Rank(Node n, int depth = 0)` (`CombatTurnPlanner.cs:777-837`) :
```csharp
// GivreClone.App/Services/Combat/CombatTurnPlanner.cs:777-784
double Rank(Node n, int depth = 0)
{
    double rank = Score(n).Total;
    if (n.Depth >= Math.Min(MaxDepth, snapshot.MaxCasts) || depth >= 2
        || n.Actions.LastOrDefault() is not { Kind: PlannerActionKind.Cast } last
        || !spells.Any(s => s.Cast.SpellId == last.SpellId && (s.Cast.IsTeleport || s.Cast.IsForcedMovement
            || s.Cast.Effects.Any(e => e.Kind is SpellEffectKind.Buff or SpellEffectKind.Summon || e.ActionId is 950 or 951 or 952)))) return rank;

    // ... exploration d'une attaque légale et abordable DANS LE MÊME TOUR (lignes 827-836)
    foreach (var sp in spells.Where(s => Affordable(n, s)))
        foreach (var f in snapshot.Fighters.Where(...))
        {
            if (Eligible(n, sp, target) && CanCast(...) && CastOn(...) is { } follow)
                rank = Math.Max(rank, Rank(follow, depth + 1));
        }
    return rank;
}
```

### Le souhait de l'utilisateur est-il exprimable aujourd'hui ?
*« Utiliser les boosts quand un kill n'est pas possible ce tour »* :  
**NON, ce n'est pas exprimable dans le moteur actuel.**
1. Si aucun ennemi n'est à portée d'attaque pendant le tour en cours (par exemple ennemi à 12 cases), `Rank(follow)` ne trouve aucune attaque exécutable ce tour : la valeur du boost retombe à 0.
2. Un score de 0 est égal au score de la racine `root` (ne rien faire). Le bot préfère donc passer son tour ou marcher sans lancer son boost.
3. Aucune clause dans `SpellConfig` ni dans le planificateur ne valorise le gain de puissance pour les tours futurs.
4. Dans `AutoSpellRotation.Build` (`AutoSpellRotation.cs:126-135`), tout sort utilitaire/support reçoit `Damage(sp) = 0`, ce qui le place **en toute fin de rotation** après toutes les attaques.

---

## 5. Arithmétique Multi-Lancers et Budgétisation PA

### La question : 2 lancers d'un sort 2 PA vs 1 lancer d'un sort 4 PA
Pour un budget de 4 PA, le planificateur compare-t-il 2 lancers d'un sort à 2 PA contre 1 lancer d'un sort à 4 PA ?

### Preuve par le code : OUI, le Beam Search l'explore
L'algorithme de recherche en faisceau (`CombatTurnPlanner.cs:936-963`) explore les séquences en augmentant la profondeur de 0 à `Math.Min(MaxDepth, snapshot.MaxCasts)` :

1. **À profondeur 0 (1er lancer) :**
   - Branche 1 : Lance le sort 4 PA (inflige 50 dégâts, reste 0 PA). Actions = `[Sort4PA]`, Rank = 50.
   - Branche 2 : Lance le sort 2 PA (inflige 30 dégâts, reste 2 PA). Actions = `[Sort2PA]`, Rank = 30.
   - Les deux branches sont insérées dans la liste `next`.
   - À la ligne 961, `beam = next.OrderByDescending(n => n.Rank).Take(BeamWidth)`. Comme `BeamWidth = 8`, la Branche 2 (rang 30) est conservée dans le faisceau aux côtés de la Branche 1.
2. **À profondeur 1 (2e lancer) :**
   - La Branche 1 n'a plus de PA et ne produit aucun nouveau nœud.
   - La Branche 2 dispose encore de 2 PA. `Casts(origin)` génère un second lancer du sort 2 PA.
   - Nouveau nœud : Actions = `[Sort2PA, Sort2PA]`, Dégâts = $30 + 30 = 60$.
   - La fonction `Consider(cast)` (`CombatTurnPlanner.cs:904-911`) compare ce score à `bestScore` :
     $60 > 50 \implies \text{best} = \text{Branche 2 (2x Sort 2 PA)}$.

### Les 5 Limites qui peuvent bloquer cette exploration
1. **La limite de profondeur fixe `MaxDepth = 3` (`CombatTurnPlanner.cs:88`) :**  
   `public const int BeamWidth = 8, MaxDepth = 3, MaxWork = 180_000;`  
   Le planificateur ne peut jamais planifier plus de 3 lancers de sorts dans une même séquence de tour. Si un personnage a 8 PA et quatre sorts à 2 PA, ou 10 PA avec deux buffs et deux attaques, la 4e action est hors de portée de l'arbre de recherche.
2. **L'élagage du faisceau `BeamWidth = 8` (`CombatTurnPlanner.cs:88, 961`) :**  
   Si le plateau offre plus de 8 options concurrentes à la profondeur 0 ayant un score immédiat $> 30$ (ex: frapper 3 cibles distinctes avec le sort 4 PA, ou différentes cases de déplacement), la branche du sort à 2 PA est éliminée dès la profondeur 0 et n'atteint jamais la profondeur 1.
3. **Le plafond de travail `maxWork = 60_000` (`LaunchService.Combat.Turn.cs:1192`) :**  
   Si le budget de calcul est épuisé (`budget.Used >= budget.Limit`), la boucle d'expansion s'arrête prématurément (`CombatTurnPlanner.cs:942, 950, 955`).
4. **La compensation heuristique dans `AutoSpellRotation.cs:105` :**  
   `if (spell.Ap == 2) dmg *= 1.25;`  
   Conscients de ce problème, les auteurs ont ajouté un multiplicateur artificiel de 1.25 aux sorts 2 PA pour compenser leur sous-classement dans la rotation classique.
5. **Le repli en rotation classique (`LaunchService.Combat.Turn.cs:1358-1388`) :**  
   Si `plannerCanAct` est faux ou si le planificateur échoue, la rotation classique s'exécute séquentiellement. Si le sort 4 PA a une priorité 1 et le sort 2 PA une priorité 2, le sort 4 PA est tiré en premier, consommant tous les PA sans aucune comparaison.

---

## 6. Audit des Mécaniques par Classe (`ClassMechanics.cs`)

Le fichier `GivreClone.App/Services/Combat/ClassMechanics.cs` (206 lignes) déclare 25 membres (constantes d'IDs de classe, records et fonctions d'évaluation tactique).

### Recensement exhaustif et statut réel
Grep croisé sur tout le code de production (`GivreClone.App`) et de tests (`GivreClone.Tests`) :

| Symbole déclaré | Ligne | Appelant Réel | Statut |
| :--- | :--- | :--- | :--- |
| `RogueBreedId = 12` | 11 | Aucun (0 appel) | **MORT** |
| `IsRogueBomb` | 13 | Aucun (templates hardcodés à `CombatTurnPlanner.cs:796`) | **MORT** |
| `GetBombWallCells` | 19 | `ClassMechanics.cs:45`, `ClassMechanicsTests.cs:13,21` | **ACTIF (Interne)** |
| `EvaluateRogueBombTactics` | 31 | `CombatTurnPlanner.cs:803` | **ACTIF** |
| `XelorBreedId = 4` | 63 | Aucun (0 appel) | **MORT** |
| `StateTelefrag = 96` | 64 | Aucun (0 appel) | **MORT** |
| `GeneratesTelefrag` | 66 | `ClassMechanicsTests.cs:26,28,31` (aucun dans App) | **MORT (Test seul)** |
| `EvaluateXelorTelefrag` | 72 | Aucun (0 appel, même pas en test !) | **MORT TOTAL** |
| `EliotropeBreedId = 15` | 81 | Aucun (0 appel) | **MORT** |
| `Portal` (record) | 83 | `ClassMechanics.cs`, `ClassMechanicsTests.cs:37` | **ACTIF (Local)** |
| `ResolveExitPortal` | 85 | `ClassMechanics.cs:112, 176` | **ACTIF (Interne)** |
| `CanHitThroughPortals` | 95 | `ClassMechanicsTests.cs:44` (aucun dans App) | **MORT (Test seul)** |
| `PortalDamageMultiplier` | 125 | `ClassMechanics.cs:179`, `ClassMechanicsTests.cs:60` | **ACTIF (Interne)** |
| `IopBreedId = 8` | 135 | Aucun (0 appel) | **MORT** |
| `FecaBreedId = 1` | 136 | Aucun (0 appel) | **MORT** |
| `SacrierBreedId = 11` | 137 | `CombatTurnPlanner.cs:809` | **ACTIF** |
| `PandawaBreedId = 12` | 138 | Aucun (0 appel) | **MORT** |
| `OsamodasBreedId = 2` | 139 | Aucun (0 appel) | **MORT** |
| `MasqueraiderBreedId = 14` | 140 | `CombatTurnPlanner.cs:816` | **ACTIF** |
| `ForgelanceBreedId = 19` | 141 | `CombatTurnPlanner.cs:821` | **ACTIF** |
| `EvaluateSacrieurCombo` | 146 | `CombatTurnPlanner.cs:814` | **ACTIF** |
| `EvaluatePandaThrowIntoBombWall` | 159 | Aucun (0 appel) | **MORT TOTAL** |
| `EvaluateIopThroughPortal` | 169 | Aucun (0 appel) | **MORT TOTAL** |
| `EvaluateZobalShieldOnSummons` | 185 | `CombatTurnPlanner.cs:819` | **ACTIF** |
| `EvaluatePandaForgelanceCluster` | 194 | `CombatTurnPlanner.cs:823` | **ACTIF** |

### Synthèse des méthodes de classe
- **Méthodes actives dans l'IA de combat (4 méthodes) :**
  1. Roublard : `EvaluateRogueBombTactics` (`CombatTurnPlanner.cs:803`)
  2. Sacrieur : `EvaluateSacrieurCombo` (`CombatTurnPlanner.cs:814`)
  3. Zobal : `EvaluateZobalShieldOnSummons` (`CombatTurnPlanner.cs:819`)
  4. Forgelance : `EvaluatePandaForgelanceCluster` (`CombatTurnPlanner.cs:823`)
- **Méthodes mortes (non câblées) :**
  - **Éliotrope :** `CanHitThroughPortals`, `PortalDamageMultiplier`, `ResolveExitPortal` ne sont jamais appelés par le moteur de combat. Pire : dans `CombatTurnPlanner.cs:60` et `LaunchService.Combat.Turn.cs:1085`, les portails au sol sont enregistrés dans `MarkedCells` comme de simples obstacles interdisant le ciblage de case !
  - **Xélor :** `GeneratesTelefrag`, `EvaluateXelorTelefrag` complètement ignorés.
  - **Synergies Panda + Roublard :** `EvaluatePandaThrowIntoBombWall` complètement ignoré.
  - **Synergies Iop + Éliotrope :** `EvaluateIopThroughPortal` complètement ignoré.

---

## 7. Plan de Correction

Classement des modifications par ratio **(Gain de dégâts / efficacité $\times$ Indice de confiance)**.  
Chaque règle est entièrement décidable à partir des données observées par le client.

```
                    MATRICE D'IMPACT DU PLAN DE CORRECTION
+-------------------------------------------------------------------------+
| Gain Dégâts |                                                           |
|             |                                                           |
|    ÉLEVÉ    |   [FIX 1] Seuil Élémentaire    [FIX 3] Valorisation Boost |
|             |   [FIX 2] Déblocage LevelUp    [FIX 4] Portails Éliotrope |
|             |                                                           |
|    MOYEN    |   [FIX 5] Profondeur MaxDepth  [FIX 6] Tri Rotation       |
|             |                                                           |
+-------------+-----------------------------------------------------------+
              |           MOYENNE          |            ÉLEVÉE            |
              +-----------------------------------------------------------+
                                    CONFIANCE
```

---

### Fix 1 : Abaissement dynamique du seuil de détection élémentaire
- **Impact :** Gain immédiat massif (élimine le gaspillage de PA sur des sorts hors-élément infligeant 0 à 8 dégâts).
- **Confiance :** 100 % (zéro risque de régression).
- **Fichier cible :** `GivreClone.App/Services/Combat/AutoSpellRotation.cs`
- **Méthode d'ancrage :** `AutoSpellRotation.Build` (lignes 62-71).
- **Règle à appliquer :** Remplacer le seuil rigide `maxStat >= 80` par un seuil adaptatif basé sur les statistiques relatives et le niveau :
  ```csharp
  // Seuil adaptatif : dès 15 points dans une stat (ou niveau du perso), détecter l'élément maître
  int dynamicMinStat = Math.Clamp(options?.Characteristics?.GetValueOrDefault(CharIds.Strength) ?? 0, 15, 60);
  bool canDetect = maxStat >= 15;
  if (options is not null && options.Elements == AutoCombatElements.Automatic && canDetect)
  {
      detectedElements = 0;
      // Un élément est retenu s'il représente au moins 40% de la meilleure stat
      int threshold = Math.Max(15, (int)(maxStat * 0.40));
      if (str >= threshold) detectedElements |= AutoCombatElements.Earth | AutoCombatElements.Neutral;
      if (intel >= threshold) detectedElements |= AutoCombatElements.Fire;
      if (cha >= threshold) detectedElements |= AutoCombatElements.Water;
      if (agi >= threshold) detectedElements |= AutoCombatElements.Air;
      if (detectedElements == 0) detectedElements = AutoCombatElements.Automatic;
  }
  ```
- **Repli si données manquantes :** Si `maxStat < 15` (personnage tout juste créé au niveau 1 sans panoplie), laisser `detectedElements = AutoCombatElements.Automatic` mais classer strictement par ratio de dégâts de base du sort.

---

### Fix 2 : Déblocage automatique des sorts au Level Up & écoute des Variantes
- **Impact :** Très élevé pour le leveling 1 -> 200 (les nouveaux sorts sont utilisables dès le combat suivant le level up sans reconnexion).
- **Confiance :** 95 %.
- **Fichier cible :** `GivreClone.App/Services/LaunchService.Packets.cs`
- **Méthode d'ancrage :** Bloc `CharacterLevelUpEvent` (lignes 1565-1571) et ajout du bloc `SpellVariantActivationEvent`.
- **Règle à appliquer :**
  1. Dans `CharacterLevelUpEvent` : consulter le catalogue de classe `_gameNames.SpellsForBreed(breedId)` et les données de sort (`spelllevelsdataroot`), ou émettre un événement de rafraîchissement forçant une réévaluation de `_charSpells`.
  2. Ajouter l'écoute du paquet `SpellVariantActivationEvent` (`LaunchService.Packets.cs`) :
     ```csharp
     if (p.Name == "SpellVariantActivationEvent")
     {
         int spellId = (int)PayloadReader.Int(p.Payload, FNum("SpellVariantActivationEvent", "spell_id"), 0);
         bool effective = PayloadReader.Bool(p.Payload, FNum("SpellVariantActivationEvent", "effective"), false);
         if (spellId > 0 && effective)
         {
             var current = _charSpells.GetValueOrDefault(p.Pid)?.ToList() ?? new List<int>();
             if (!current.Contains(spellId)) current.Add(spellId);
             // Retirer l'autre variante de la paire
             var pair = _gameNames.SpellVariantPair(spellId);
             foreach (int other in pair) if (other != spellId) current.Remove(other);
             _charSpells[p.Pid] = current;
             CharacterSpellsUpdated?.Invoke(p.Pid, current);
         }
     }
     ```
- **Repli si données manquantes :** Conserver la liste précédente de `SpellsEvent` sans lever d'exception.

---

### Fix 3 : Valorisation tactique des Boosts hors-portée / sans Kill
- **Impact :** Élevé (permet à l'Éliotrope ou au Iop de se booster au tour 1 quand l'ennemi est loin).
- **Confiance :** 90 %.
- **Fichier cible :** `GivreClone.App/Services/Combat/CombatTurnPlanner.cs`
- **Méthode d'ancrage :** `CombatTurnPlanner.Score` (lignes 452-463) et `Class` (lignes 900-903).
- **Règle à appliquer :**
  1. Dans `Score(Node n)` : si le tour ne permet pas d'infliger de dégâts directs (`n.Damage == 0`), mais qu'un sort d'auto-boost (ex: Puissance, Focus, Tir Éloigné) a été lancé avec succès, attribuer un `NextTurnBuffScore` (ex: 20 points).
  2. Ajuster la classification des nœuds prioritaires (`Class`) :
     ```csharp
     int Class(Node n, PlannerScore s) => aggressive
         && (s.Damage > 0 || s.Finishes > 0 || s.Healing > 0 || s.Shielding > 0 || s.Control > 0
             || (n.Actions.Any(a => a.Kind == PlannerActionKind.Cast && IsOffensiveSelfBuff(a.SpellId))))
         && Math.Max(0, Exposure(n, n.Cell) - n.Shield[self]) * 2 < n.Life[self] ? 1 : 0;
     ```
- **Repli si données manquantes :** Si aucun boost n'est admissible ou abordable, le score reste à 0 et l'IA priorise le placement sécurisé.

---

### Fix 4 : Câblage des Portails Éliotrope dans le Planificateur
- **Impact :** Spécifique Éliotrope maximal (transforme une classe injouable en IA en un sniper longue portée).
- **Confiance :** 85 %.
- **Fichier cible :** `GivreClone.App/Services/Combat/CombatTurnPlanner.cs`
- **Méthode d'ancrage :** `Casts(Node n)` (lignes 750-764) et `Rank(Node n)` (lignes 806-825).
- **Règle à appliquer :**
  1. Lire les portails actifs du lanceur sur le plateau (détectables via `_fightMarks` ou templates d'invocations de portails).
  2. Si `selfBreed == ClassMechanics.EliotropeBreedId` et que le réseau compte au moins 2 portails :
     - Utiliser `ClassMechanics.CanHitThroughPortals` pour autoriser le ciblage des cellules hors portée directe mais accessibles via les portails.
     - Appliquer le coefficient `ClassMechanics.PortalDamageMultiplier(netDistance)` lors de l'estimation des dégâts de ces branches.
- **Repli si données manquantes :** Si moins de 2 portails sont posés, appliquer le calcul classique de portée et ligne de vue directe.

---

### Fix 5 : Profondeur adaptative du Beam Search (`MaxDepth` étendu)
- **Impact :** Moyen-Élevé (optimise les tours à 8-12 PA en sorts 2 et 3 PA).
- **Confiance :** 95 %.
- **Fichier cible :** `GivreClone.App/Services/Combat/CombatTurnPlanner.cs`
- **Méthode d'ancrage :** Déclaration de `MaxDepth` et boucle principale de recherche (lignes 88 et 937).
- **Règle à appliquer :** Rendre la profondeur maximale dépendante du budget PA disponible :
  ```csharp
  int minApCost = spells.Length > 0 ? spells.Min(s => s.Cast.Ap) : 3;
  int dynamicMaxDepth = Math.Clamp(snapshot.Ap / Math.Max(2, minApCost), 3, 5);
  for (int depth = 0; depth < Math.Min(dynamicMaxDepth, snapshot.MaxCasts) && beam.Count > 0 && budget.Used < budget.Limit; depth++)
  ```
- **Repli si données manquantes :** Le contrôle existant `budget.Used < budget.Limit` garantit l'arrêt avant dépassement de temps ou mémoire.

---

### Fix 6 : Cohérence du tri en Repli de Rotation Classique
- **Impact :** Moyen (évite les lancers stupides lorsque le planificateur n'a pas pu agir).
- **Confiance :** 98 %.
- **Fichier cible :** `GivreClone.App/Services/LaunchService.Combat.Turn.cs`
- **Méthode d'ancrage :** Boucle de rotation classique (lignes 1358-1388).
- **Règle à appliquer :** Avant de parcourir la rotation classique, filtrer les sorts dont les dégâts réels estimés sur la cible principale sont nuls ou inférieurs à 10 % des dégâts optimaux du tour, évitant de vider les PA sur un sort hors-élément.
- **Repli si données manquantes :** Conserver la liste ordonnée par `Priority`.

---

## Conclusion et Recommandation de Déploiement

Pour satisfaire immédiatement la demande de l'utilisateur (*« quand il va débloquer il faut utiliser les meilleurs sorts »*), l'ordre d'implémentation conseillé est :
1. **Étape 1 (Immédiate) :** Appliquer le **Fix 1** (`AutoSpellRotation.cs`) pour abaisser le seuil `maxStat >= 80` à 15. Cela supprime à 100 % le problème des sorts hors-élément lancés pour 5 dégâts dès le niveau 1.
2. **Étape 2 (Cycle de vie) :** Appliquer le **Fix 2** (`LaunchService.Packets.cs`) pour brancher l'écoute des nouveaux sorts au level up et l'événement `SpellVariantActivationEvent`.
3. **Étape 3 (Tactique Éliotrope & Boosts) :** Appliquer les **Fix 3 et 4** dans `CombatTurnPlanner.cs` pour valoriser les boosts hors combat immédiat et brancher les portails Éliotrope déjà codés dans `ClassMechanics.cs`.
