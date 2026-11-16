# Synthèse — recherche Dofus / Givre, 16 octobre 2026

Recherche statique : aucun build, lancement ou réseau. Rapports et preuves détaillées : [lanceur / Zaap](01_ankama_launcher.md), [champs protobuf](02_dofus_champs_obfusques.md), [Givre / clone](03_recap_givreactive.md).

Préfixes des preuves : **M/** = `%USERPROFILE%\Videos\GIVRE\GivreShareWork\` ; **S/** = `%USERPROFILE%\Videos\GIVRE\GivreActive\` ; **R/** = `%USERPROFILE%\Videos\GIVRE\DofusReverseIndexer\local-output\`.

## Ce qui est établi

- **Le lancement officiel est reconstructible sous forme paramétrée**, à partir des modèles installés et du batch, sans lire de valeurs de session. Preuves : `%USERPROFILE%\AppData\Local\Ankama\Dofus-dofus3\zaap.yml:4-27`, `zaap-windows.yml:3-5` et `zaap-start.bat:16` dans le même dossier ; rapport 01.
- **Les sources protocole divergent.** Le catalogue clair et le proto candidat 3.7.11.11 ne concordent pas sur plusieurs messages ciblés. Son manifeste déclare les sémantiques et l'encodage exact non certifiés : `R/historical-extractions-v3/3.7.11.11/manifest.json:2-18`. Deux snapshots identiques ne prouvent pas le protocole du client exécuté ; rapport 02.
- **Un champ exigé manque : `ClientChallengeInitRequest.fyvk`.** Recalcul local des 505 couples de `M/GivreClone.Protocol/RequiredMapping.cs:10-517` contre `M/data/fields.json` : ce seul couple est absent ; exigence à la ligne 99. Cela ne donne ni son numéro réel ni son rôle.
- **Le code bancaire peut annoncer un succès sans confirmation métier** (`M/GivreClone.App/Services/LaunchService.Bank.cs:128-133,263-266`). L'HDV attend déjà un événement, mais sa décision locale porte sur sa catégorie (`LaunchService.Hdv.cs:165-184`, même dossier). Givre apporte des mécanismes utiles, mais son dataset cible **3.7.7.6** (`S/DataSets/3.7.7.6/givre-data-set-manifest.json:3-5`) ; comparaison dans le rapport 03.

## Ce qui reste NON VÉRIFIÉ

Valeurs d'un lancement réel ; appels et replis effectivement exécutés par Unity 3.7.11.11 ; priorité de `connectionHosts` / `configUrl`, hôte choisi et obligation alléguée de `hosts` ; acceptation serveur selon Shield ; sémantique des champs en conflit ; séquences exactes personnage/tutoriel ; compilation, gameplay et performances actuels. Raisons : corps client insuffisamment couverts, sources contradictoires et aucune exécution dans cette mission. Les rapports détaillent chaque limite.

## Cinq actions suivantes, par gain attendu

1. **Réconcilier identité, code, numéro, type et usage des messages** avant de câbler quête/challenge/PNJ. Ne pas inventer l'alias `fyvk`. Gain : éliminer les erreurs à la racine du transport. Preuves : manifeste candidat précité et rapport 02.
2. **Aligner l'intégration du lanceur sur son contrat officiel**, puis retrouver les consommateurs client avant toute décision de redirection. Gain : connexion reproductible ; rapport 01.
3. **Confirmer les dépôts bancaires par un état serveur corrélé**, avec issue inconnue explicite. Gain : fiabilité du farm ; `LaunchService.Bank.cs:128-133,263-266` et rapport 03.
4. **Corréler l'HDV par objet, quantité, prix et génération**, sans répétition aveugle après délai. Gain : éviter doubles tentatives ; `LaunchService.Hdv.cs:165-184` et rapport 03.
5. **Porter les garde-fous ciblés de carte/récolte**, en conservant ceux du clone et en requalifiant les données .11.11. Gain : moins d'actions sur état périmé ; sources comparées du rapport 03.

Gains attendus, non mesurés. Aucune de ces modifications n'a été effectuée.