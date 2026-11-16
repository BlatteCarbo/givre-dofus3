# UI Socket et bandeaux de version — 20 octobre 2026

## Diagnostic HEAD 4c8b501

La revue locale croisée Dofus 3.7.11.11 → 3.7.12.12 peut produire une décision
`ReviewedCrossPatchCandidate` après validation de la version installée, de
l'empreinte du manifeste et du Protobuf identique. Le commit 4c8b501 avait
déjà ajouté un cas `LocalVersionNotice.Compare(..., socketAdmission)` qui
retirait l'alerte **version différente** pour cette admission.

Deux problèmes UI subsistaient :

1. `VersionNoticeVisible = versionNotice.RequiresAttention || newerRelease`
   réaffichait le bandeau pour une simple version **publiée**, même lorsque
   le client effectivement installé et le mapping avaient été revus.
2. `RefreshProtocolAuditAsync` forçait `ProtocolAuditVisible = true` au
   démarrage, puis concaténait un long compte rendu et les actions de l'indexeur
   dans un second bandeau permanent.

Le contrôle d'empreintes locales `ProtocolObservedDrift` conserve son sens :
une différence entre deux observations n'est pas une preuve de changement
de wire, mais reste un **motif de revue**. Un changement entre l'ancienne
version observée et le nouveau client **maintenant revu et épinglé SHA**
n'est pas, en soi, un blocage. Une divergence non revue, ou une divergence
contre une référence **fiable**, ne peut jamais être masquée.

## Nouvelle politique d'affichage (sans masquer les vrais défauts)

Le bandeau ambre reste actif pour :

- Version installée absente, marqueur invalide ou bundle non déclaré ;
- Version tierce installée sans décision de revue correspondant au client
  courant, par exemple 3.7.13.13 avec mapping 3.7.11.11 ;
- Empreintes Dofus modifiées depuis le repère local **sans** nouvelle admission
  SHA courante, ou `HASH_CHANGED` contre une référence fiable (toujours en
  alerte) ;
- Erreur de diagnostic local empêchant toute conclusion.

Un client installé en 3.7.12.12 avec un mapping source 3.7.11.11 et une
**admission locale SHA / version calculée sur l'installation courante**, sans
changement d'empreintes observé, n'affiche plus ce bandeau. Une mise à jour
simplement **annoncée** sur le site officiel est conservée dans les détails
de protocole, mais ne se présente plus comme un défaut bloquant du client
installé. Aucun changement des gardes SocketProbe, des permissions ni de la
validation runtime n'est induit par cette décision de présentation.

Les détails techniques s'ouvrent volontairement depuis **Outils → Protocole**
ou via **Détails** dans une vraie alerte. Ils ont une hauteur limitée, un
bouton « Revérifier » et « Fermer ». Les actions explicites « Plan indexeur »
et « Extraire / comparer » demeurent sous Outils plutôt qu'en permanence sous
le titre. L'extraction nécessite toujours un clic explicite.

## Capacités Socket : observation distincte de l'admission

Le menu Outils affiche en lecture seule, pendant sa seule ouverture :

- Sessions Socket réelles (et non mode Socket mémorisé) : en jeu, attente,
  déconnectées ;
- Nombre de canaux enregistrés / attente de carte / blocages, basé sur
  `AccountsVM.ObserveSocketControl(AccountRow)`, fourni par worker-19 ;
- Admission de version/mapping et avertissement de non-certification des
  commandes gameplay et ACK.

Le récapitulatif est rafraîchi toutes les deux secondes **uniquement quand
Outils est ouvert** ; aucune connexion, aucun PID, aucune session, aucun bot
ou client Dofus n'est créé, interrompu ou piloté. Le nom des comptes, adresses
IP et autres données de session ne sont pas exposés dans le récapitulatif.

Les états `channel-registered` et « En jeu » ne prouvent pas un mouvement,
un combat ni une expédition réussis. L'interface l'énonce explicitement :
les ACK et réponses du serveur font foi, pas la pastille de connexion.

## Fichiers et validation

OWN modifiés uniquement : `GivreClone.App/Views/MainWindow.axaml(.cs)`,
`ViewModels/MainViewModel.ProtocolAudit.cs`, `MainViewModel.Version.cs`,
`Services/LocalVersionNotice.cs`, et tests
`GivreClone.Tests/LocalVersionNoticeTests.cs`. Aucun changement sur
SocketGameClient, SocketProbe, AccountsViewModel, panels secondaires,
MainViewModel général ou projets csproj.

Tests ajoutés : la revue croisée validée fait disparaître l'alerte,
mais une différence SHA fiable reste prioritaire ; la version tierce installée
sans admission reste en alerte ; le repère historique modifié reste un warning
si le client courant n'a pas de nouvelle admission SHA. Au contraire, une
ancienne version observée différente ne doit pas produire de faux bandeau
contre l'installation désormais revue et épinglée.

**Build App ciblé** : PASS, 0 erreur / 198 avertissements de dépôt :

    dotnet build GivreClone.App/GivreClone.App.csproj --no-restore -c Release -v:q -clp:ErrorsOnly -p:BuildProjectReferences=false -p:DefaultExcludesInProjectFolder=**/obj/**%3B**/bin/**%3B**/.*/**

**Tests** : 15/15 PASS, aucun échec, avec la commande ciblée :

    dotnet test GivreClone.Tests/GivreClone.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~LocalVersionNoticeTests -v:q -p:BuildProjectReferences=false -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false

Le premier test standard était bloqué par un type manquant dans le test
`SocketLuaControlTests.cs` pendant un lot parallèle ; une fois ce type arrivé,
des attributs d'assembly dupliqués dans le projet partagé `Tests/obj`
empêchaient le build standard. Les propriétés MSBuild ci-dessus ont permis
l'exécution ciblée **sans modifier** les projets, les caches ou les fichiers
d'autres workers. Une compilation totale de la solution et la vérification
visuelle sous thème clair/sombre et skin Classic restent à la charge du prime.
