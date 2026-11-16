# M25 — Founoroshi : interruption, glyphe de feu et portail Éliotrope

Étude documentaire ciblée, 4 novembre 2026. [Source de modifications 3.7](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), rubrique donjons/monstres. [Catalogue des boss 3.7.12](https://dofus-grimoire.fr/boss/) pour poursuivre la recherche du déclencheur exact. Aucune vidéo YouTube ou capture de plateau n'a été étudiée pour ce cas. Aucun test d'IA ou session de jeu exécuté.

## Ce que l'on sait par écrit

Deux modifications 3.7 sont rapportées pour Founoroshi :
1. Le fait de bloquer le boss pendant **un tour complet** ne produit plus la mise à mort immédiate des personnages au contact qui caractérisait l'ancien comportement.
2. Les **glyphes de feu ne désactivent plus les portails de l'Éliotrope**.

Statut pour les deux : `corrobore-source` pour le **texte communautaire décrivant le correctif 3.7**, mais ordre exact, identifiants d'effets et portée du sous-patch 3.7.13.17 non reproduits. Éviter d'en déduire que rester au contact est toujours sans risque ou que toutes les zones de feu interagissent pareil avec les portails.

## Pourquoi cette mécanique est stratégique

Une vieille stratégie peut estimer que le portail devient inutilisable sous une zone de feu, puis condamner un allié à rester au contact ; un simulateur 3.7 doit garder un état **observé** de chaque portail, et calculer la survie avec les résistances, les sorts, l'initiative et le tour effectif du boss.

### Machine à états candidate

```text
S0: boss actif, portail P ouvert/fermé observé, zone de feu G annoncée
 ├─ G apparait/est active → observation de P (ne pas changer arbitrairement l'état)
 ├─ blocage du boss pendant un tour entier → état/attaque possible recontrôlés
 └─ prochain tour du boss → effets réellement observés, mise à jour du danger
```

## Deux plans et leurs risques

- **A — duo Éliotrope/Féca** : l'Éliotrope gère une route de retraite ; le Féca couvre l'allié potentiellement exposé. Si G touche P, la route n'est fermée que si un événement confirmé le prouve ; si une attaque ennemie rend la zone mortelle, prendre une autre sortie.
- **B — trio Pandawa/Éliotrope/Crâ** : un placeur garde la menace au loin, l'Éliotrope ouvre une direction de tir et le Crâ exploite uniquement un portail dont la route est confirmée. Si les ennemis traversent aussi les portails, une route qui augmente les dégâts peut augmenter le danger.

Quatuor : ajouter un support Eniripsa pour pallier les dégâts continus plutôt qu'anticiper l'ancienne exécution au contact. Ce sont des **exemples proposés** et non des replays de ce donjon.

## Scénarios discriminants

- **P01** : G appliqué à P sur cible 3.7 ; le portail reste disponible si l'information et le serveur confirment l'état ouvert.
- **N01 version** : sur une vidéo antérieure à 3.7, le portail pourrait se fermer. Consigner la divergence et interdire la transposition brute.
- **N02 ordre** : G apparaît après le tour Éliotrope ; le plan de fuite du prochain acteur doit lire le portail actuel.
- **N03 état inconnu** : aucun événement de portail disponible après G ; ne pas le marquer ouvert ni fermé par défaut.
- **N04** : blocage incomplet du boss ; ne pas appliquer la règle « un tour complet » à un simple tacle d'une action.

Ces tests sont proposés, non exécutés. Pour les rendre précis, il manque la carte, la nature du glyphe, les tours et les messages de changement d'état réellement émis.

