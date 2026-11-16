# No Anim et déplacements en combat — revue du 20/10/2026

## Situation prouvée

GivreWork avait déjà les cases `NoAnim`, `Collapse`, `InstantMove` et `Tactical`
dans l'onglet Combat ainsi que leur sérialisation dans `CombatProfile`.
Le code autonome `NativeTurboRuntime` sait charger un **skipper Animator2D**
via un hôte natif dont les binaires sont épinglés par SHA-256. Cette preuve
atteste l'identité des fichiers et l'existence d'un protocole de contrôle
`STATUS`, **pas** un fonctionnement client/serveur en jeu.

Le clone isolé **ne charge jamais** le `GivreMods.dll` historique non épinglé.
Le standalone `DofusCombatAnimationSkipper.dll` sait traiter les animations,
mais **aucune implémentation indépendante vérifiée n'a été trouvée** pour :

- `Collapse` : réduire toutes les temporisations de séquences ;
- `InstantMove` : afficher immédiatement le combattant sur sa cellule finale.

Ces deux options sont désormais bloquées pour le clone au point de commande
et dans l'UI. Elles ne sont **pas** remplacées par un faux ordre de téléportation
réseau ; en particulier, rien ici ne permet de choisir une cellule sans action
de déplacement autorisée par Dofus. Les préférences historiques sont conservées
dans les profils, mais ne sont pas promues en effets natifs.

## Nouveau comportement

- Bouton **No Anim (profil rapide)** : applique en un seul push `NoAnim=true`,
  `Collapse=false`, `InstantMove=false`, à condition que les modules
  autonomes épinglés soient présents, que l'option native soit activée et que
  le droit NoAnim soit disponible. Ne bascule pas en Turbo et ne change pas les
  réglages d'ACK/transport.
- Bouton **Animations normales** : rétablit les trois options à false ;
  fonctionnement jeu seulement si le contrôleur natif prend la mise à jour.
- Bouton **Vérifier les modules** : calcule les quatre SHA-256 depuis le dossier
  de build ; **aucune injection, processus, compte ni requête réseau**.
- Le host historique n'entraîne plus une attente artificielle de 4,8 s dans
  le clone avant le fallback standalone.

## Stage des binaires détenus par le propriétaire (test hors jeu)

Un clone du dépôt Git ne transporte pas nécessairement les binaires natifs
compilés. La compilation `.NET` seule ne les garantit donc pas.
Le script `tools/Stage-CombatVisualModules.ps1` identifie six binaires
préexistants dans le **projet local GivreWork** (jamais GivreActive),
contrôle leurs empreintes strictes **avant toute copie**, et les place
uniquement sous `GivreClone.App/bin/Release/net10.0/NativeTurboRuntime`.

La source bootstrap s'appelle `DofusNativeBootstrap.dll` mais le staging la
renomme `GivreTurboBootstrap.dll`, nom attendu par le runtime.
SHA-256 épinglés dans `CombatVisualModPolicy`/`NativeTurboRuntime`.
Pas de DLL tierce récupérée depuis `dofus-miniatures`, dépôt sans licence
générale déclarée. Aucun fichier Dofus, de GivreActive ou de MizanBot
n'est écrit par le script.

Commande locale :

```powershell
pwsh -NoProfile -File tools/Stage-CombatVisualModules.ps1 -Configuration Release
```

**Vérification effectuée** : six lignes `PINNED`, reçu `STAGED`; contrôle
SHA-256 sur l'ensemble des sources avant le premier écrit. Cela n'est **pas**
une preuve d'effet visuel dans le client. La version 3.7.12.12 reste
**non installée et BLOCK pour l'admission runtime**, malgré les fichiers
`dofus.proto` de sources tierces identiques à 3.7.11.11.

## Validation et reste à faire

`CombatVisualModPolicyTests` couvre refus clone historique, permission,
mode Socket, absence de TP serveur, altération des fichiers et chemins hors
racine. La suite `GivreClone.PureTests` assure la CI sans charger un client.

À certifier plus tard sur un **client de test dédié et possédé** : identité
PID/chemin/date, exact build, chargement du host (STATUS confirmé), animation
réellement écourtée, restauration après combat et déconnexion, rollback sans
DLL persistante. Une authentique implémentation de `InstantMove` nécessitera
un adaptateur natif indépendant de la version, des captures et un banc
de test visuel spécifique avant tout déblocage.
