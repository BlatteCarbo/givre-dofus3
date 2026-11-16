# Presets banque et scan HDV - validation du 3 novembre 2026

## Banque et recolte

Le modele `recolte-banque` separe `move()` (recolte et sorties) de
`bank()` (depot). Les tests executent ces fonctions avec MoonSharp.
La permission Gather est reverifiee dans `BankDepositAllAsync` apres
l'ouverture asynchrone et avant le transfert. Une revocation refuse
le depot, mais execute toujours la fermeture du coffre dans `finally`.

Le test `Deposit_refuses_permission_revoked_during_bank_open_and_still_closes`
declenche la revocation au retour de StorageInventoryContentEvent.
Le double de licence utilise un etat propre au contexte du test, partage
entre ses continuations. Sans le controle final, quatre envois sont
observes au lieu des trois attendus (dialogue, reponse, fermeture).
Avec le controle, aucun transfert ne part et le sac reste intact.

## Scan HDV

Le modele `hdv-scan` lit uniquement `eco:price(gid)`, sur les prix deja
connus pour le personnage/serveur courant. Il ne rafraichit pas les prix,
ne voyage pas et ne contient aucun ordre d'achat. Il lit un objet par
appel, au maximum vingt, puis renvoie false, y compris aux appels suivants.
Un prix absent est signale comme inconnu, pas remplace par zero.
La liste SCAN_GIDS est locale au script, pas un reglage moteur.

Les trois cas MoonSharp couvrent une liste vide, trois objets et une liste
de vingt-cinq objets tronquee a vingt. Seules les lectures et sorties console
sont disponibles dans ce montage. Une mutation du plafond a vingt et un
a fait echouer le cas de depassement pour la raison attendue ; elle est restauree.

## Resultats et limites

Commande finale :

```text
dotnet test GivreClone.Tests -c Release --no-restore --filter "FullyQualifiedName~LuaTooling|FullyQualifiedName~F08InventoryBankTests|FullyQualifiedName~MZP27BankDepositTrackerTests" --verbosity minimal
```

Resultat : 359 tests, 359 reussis, zero echec, zero ignore, code de sortie 0.
La suite elargie avait identifie deux problemes introduits par le nouveau
modele : le plafond du nombre de modeles et une configuration de script
declaree globale. Tous deux ont ete corriges avant ce resultat final.

Ces preuves ne constituent pas un trajet bancaire teste en jeu ni une
validation visuelle du selecteur de modeles. Le serveur de langage C#
echoue a enumerer `artifacts/kourial-tools/pypdf` (acces refuse) ; la
compilation et les tests .NET constituent ici les diagnostics disponibles.
## Preparation FM

Le module expose maintenant `fm:prepare(objet, jobId)` et le modele
`fm-preparation`. L'audit lit le support du sac, les niveaux metier observes,
les competences de modification de la base locale et la liaison de l'objet.
Il ne consomme aucune rune et ne confirme pas l'acces a un atelier.
Un manque de metadonnees reste un refus, pas une compatibilite supposee.

Les tests `FmPreparationControlTests` utilisent SQLite et l'hote Lua reel :
support niveau 10 avec metier 64 niveau 10 accepte ; niveau 9, metier 63 et
niveau inconnu refuses. Les quatre cas ont passe. Une mutation remplacant
le niveau observe par 200 a fait echouer exactement le cas niveau 9 ; le
code est restaure et la validation finale a passe : 335 tests, zero echec,
zero ignore, code de sortie 0. Les tests de l'hote
couvrent aussi le support absent, le contexte manquant et la fin du modele.
La compilation et 351 tests Lua/compatibilite avaient passe avant cette mutation.

## Rapport net : contrat a respecter

`HdvStatsStore` distingue les ventes encaissees et les taxes payees, mais
ne journalise pas le cout d'acquisition des objets vendus. Leur difference
peut etre presentee comme encaissement HDV apres taxes, pas comme benefice
commercial complet. `stats:summary` expose des gains et une duree bottingSec,
pas une comptabilite complete des depenses. Le preset `rapport-kamas` utilise
`stats:hdvSummary` et distingue explicitement le cout d'acquisition inconnu.
Le preset `rendement-horaire` divise les quantites par bottingSec, sans duree
supposee. Les 24 tests cibles ont passe apres restauration ; ignorer la duree
avait fait echouer les deux cas demi-heure et deux heures.

## Classement prudent

`classement-prudent` classe les actions de recolte par passage dans les cartes
de la heatmap locale de la semaine (60 cartes maximum). Le seuil descriptif
est cinq visites, et deux cartes admissibles sont necessaires. Ce seuil ne
constitue pas une garantie statistique ni une mesure de benefice en kamas.
Le script ne transmet aucun ordre au jeu.

La mutation du seuil a une visite a fait echouer les deux cas limites. Apres
restauration, 27 tests cibles ont passe, zero echec, zero ignore, code 0 :
PrudentRankingTemplateTests, StatsMapSummaryControlTests, HourlyYieldTemplateTests,
HdvSummaryControlTests et LuaContentTests. Le test du veritable hote Lua charge
le format persiste avec deux cartes et verifie que 99 et 40 unites recoltees
comptent comme deux actions, pas 139. Le selecteur graphique des nouveaux
presets et leur utilisation sur un historique de jeu reel restent non verifies.
