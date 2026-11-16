# M30 — Protozorreur et Krobes : délais conditionnels et mort automatique, version 3.7

**Recherche documentaire du 04/11/2026.** Périmètre : Ventre de la Baleine, mécanique de Restauration/Infection, Malamibe et changement de délais en 3.7. Référence client locale du kit : 3.7.13.17, non contrôlée par manifeste. Aucun combat, code, test ni compte utilisé.

## Résultat prioritaire

Le patch **3.7** abaisse **de 5 à 3** le nombre de tours nécessaires à la mort conditionnelle d'un **Krobe** et **de 10 à 6** pour le **Protozorreur**. La condition doit toujours être respectée. Un indicateur du nombre de tours restant avant mort est affiché avec l'état invulnérable d'un Krobe.

**Source 3.7 datée 04/10/2026** : [DofusPourLesNoobs, Mise à jour 3.7, section Krobes et Protozorreur](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Recoupement indépendant [Dofuserie, bilan de la 3.7](https://www.dofuserie.com/mise-a-jour-3-7-dofus/).

Le [guide Ventre de la Baleine de DofusPourLesNoobs](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html) a été partiellement mis à jour pour **3 tours / 6 tours** : ses paragraphes utilisent ces nombres, mais l'en-tête du tableau indique encore « 5 tours consécutifs ». **Cette ligne est obsolète pour la 3.7**. Les guides [Dofus pour les Pros](https://dofuspourlespros.com/ventre-de-la-baleine.html) et [JeuxOnline, mis à jour 2019](https://dofus.jeuxonline.info/article/14644/ventre-baleine) décrivent encore 5/10 : uniquement contexte mécanique historique, jamais source des nouveaux délais.

## Sources et méthodes vérifiées

| ID | Source | Rôle et limite |
| --- | --- | --- |
| M30-S1 | [MàJ 3.7, 04/10/2026](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | **Version cible 3.7** : 3 tours Krobe, 6 boss, compteur visible sur invulnérable. |
| M30-S2 | [Ventre de la Baleine, DofusPourLesNoobs](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html), § Dislocation, Restauration, Principe boss | Détaille les conditions par espèce, reset du compteur si condition manquée, Malamibe et glyphe infectieux ; **reliquat d'en-tête ancien « 5 tours »**. |
| M30-S3 | [Dofuserie, actualité 3.7 d'octobre 2026](https://www.dofuserie.com/mise-a-jour-3-7-dofus/) | Recoupe la réduction 3 et 6 ; aucune preuve des phases exactes d'événements serveur. |
| M30-S4 | [JeuxOnline Ventre de la Baleine (2018–2019)](https://dofus.jeuxonline.info/article/14644/ventre-baleine) | Anciennes conditions de restauration ; mort après 5/10 **obsolete-pour-cible**, hypothèses de phase à revalider. |
| M30-S5 | [Dofuthèque, Ventre de la Baleine](https://www.dofutheque.com/ventre-de-la-baleine) | Décrit l'interception par Malamibe et les conditions de classe ; certaines rubriques affichent encore **5 tours** pour Virustine, à écarter en 3.7. |

**Vidéos :** la recherche de tutoriels YouTube Protozorreur/Krobes 2025/2026 n'a pas abouti à une URL vidéo spécifique suffisamment vérifiable pour servir de preuve. Aucun appel MCP get_video_info/get_timed_transcript M30 avec URL authentifiée n'a donc été possible. **Aucun horodatage vidéo ni observation de plateau inventé.** Ce blocage est consigné comme lacune ; le rapport repose sur les textes de règles versionnés, sans statut declare-video ou observe-video. Les systèmes MCP YouTube avaient répondu en erreur « YouTube is blocking requests from your IP » pour plusieurs vidéos des missions précédentes ; cela ne prouve pas que tous les futurs liens M30 sont inaccessibles.

## Règles atomiques et statut de preuve

| ID | Règle documentée | Statut | Références et réserve |
| --- | --- | --- | --- |
| **M30-R01** | Toute entité Krobe est **invulnérable aux attaques ordinaires** ; une Malamibe invoquée intercepte les dommages de ces attaques. | **corrobore-source** | [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html), section Dislocation. Si Malamibe disparue, comportement d'une frappe suivant sa mort à lire du serveur. |
| **M30-R02** | Un Krobe est **éliminé automatiquement après trois validations de sa condition** ; si une validation manque, le **compteur retombe à zéro** dans le guide actuel. | **corrobore-source** patch + guide | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). Moment précis de calcul/tick et d'élimination à vérifier par événement. |
| **M30-R03** | **Bacterrib :** au **début de son tour**, aucun adversaire à **5 PO ou moins**. | **corrobore-source** | [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). Une source historique parle de « plus de 6 PO » ; ambiguïté de distance et règle d'invocation à tester, la source récente prévaut provisoirement. |
| **M30-R04** | **Pataugerme :** doit être frappé en **Terre, Feu, Eau et Air** au cours du cycle de validation ; Neutre ne remplace pas un élément. | **corrobore-source** | [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html), [S4 historique](https://dofus.jeuxonline.info/article/14644/ventre-baleine). Les frontières exactes de son « tour » et les sources de dégâts admissibles sont à instrumenter. |
| **M30-R05** | **Tabacille :** **aucun soin** pendant trois tours ; selon le guide, **le vol de vie est permis**. | **corrobore-source** condition et exception guide | [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). Soin automatique d'objet/compagnon et exceptions vol de vie exigent replay. |
| **M30-R06** | **Verminocule :** au **début de son tour**, un **personnage joueur** à son contact ; les **invocations ne comptent pas**. | **corrobore-source** | [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). Statut d'un compagnon contrôlé à confirmer séparément. |
| **M30-R07** | **Virustine :** elle ne doit être **ni déplacée ni ciblée par une tentative de retrait PM**, y compris déplacement par allié ennemi ; les **dégâts de poussée sans déplacement effectif** peuvent entraîner un retrait PM spécial sans reset, selon guide. | **corrobore-source** pour interdit ; **hypothese** pour application exacte de l'exception au client cible. | [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). Échec de retrait esquivé peut lui-même invalider la condition ; à tracer. |
| **M30-R08** | **Protozorreur :** la mort automatique requiert **six tours consécutifs** en état **Infection** (3.7). Pour l'infecter : glyphe violet créé sous un personnage blessé ; boss attiré par sa propre première frappe de tour vers ce glyphe, qui applique Infection **3 tours**. Renouveler avant expiration. | **corrobore-source** 3.7 pour six ; détails positionnels issus [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). | Déclenchement précis du compte, seuil d'expiration et mort par tour du poseur ou boss encore inconnus. |
| **M30-R09** | La Malamibe, invoquée par le premier ennemi dans l'ordre d'initiative, intercepte les dégâts et présente un **danger de mort d'équipe si vivante deux tours complets** ; réinvoquée au tour de l'ennemi suivant après sa mort. | **corrobore-source** [S2](https://www.dofuspourlesnoobs.com/ventre-de-la-baleine.html). | Horloge précise du danger, conditions du challenge et éventuels écarts du sous-patch local à valider. |
| **M30-R10** | L'état d'invulnérabilité affiche désormais un **indicateur du nombre de tours restant** pour chaque Krobe. | **corrobore-source** [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). | Une infobulle/UI n'est pas une confirmation serveur d'élimination. |
| **M30-R11** | Les vieux guides indiquent **5 tours Krobe / 10 Protozorreur**, incompatibles avec la mise à jour 3.7. | **contradictoire**, **obsolete-pour-cible** | [S4](https://dofus.jeuxonline.info/article/14644/ventre-baleine) versus [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html). Ne pas « moyenner » les durées. |

## Machine à états minimale

**Krobe k** : état **ALIVE_INVULNERABLE**, compteur consécutif c(k)=0..3 et predicate P(k,t). À la phase de validation réelle, si P vrai : c←c+1 ; sinon : c←0 ; si c=3 : transition **CONDITIONAL_DEATH** et retrait du combat. Les règles P diffèrent par Krobe (R03–R07). Le tick **n'est pas arbitrairement** le début du tour de k pour toutes les espèces : Bacterrib et Verminocule ont précisément ce déclencheur, les conditions globales de Pataugerme/Tabacille/Virustine peuvent avoir une autre phase ; à lire du serveur. La mort par compteur **n'est pas un dommage** : ne pas essayer de la calculer sur PV.

**Protozorreur b** : état **UNINFECTED** (c=0) / **INFECTED** (c=1..6, expiration de son état) / **CONDITIONAL_DEATH**. Un glyphe infectieux valable + déplacement déclenché du boss autorise INFECTED ; maintenir chaque fenêtre jusqu'à six validations ; perdre Infection remet c à 0 selon description de restauration, à vérifier via trace. Le compte doit être attaché au **boss UID et à son tour/cycle**, pas à la dernière case du glyphe.

**Malamibe m** : **ABSENT → SUMMONED(active timer) → DEAD → RESUMMON_PENDING → SUMMONED**. Conserver UID de chaque instance, initiative du réinvocateur, intercept des dégâts à l'instant de la frappe, et danger d'équipe lorsque le délai atteint son seuil. Un ACK d'attaque n'est pas une preuve de décès ni d'interception.

## Compositions solo, duo, trio, quatuor et initiative

- **Solo** : Bacterrib à distance >5 PO lors de **chacun de ses débuts de tour** ; une téléportation ennemie juste avant peut faire échouer. Tabacille interdit les soins de survie ; l'agent doit comparer probabilité de survie et avancement compteur, et choisir le repli si PV critiques.
- **Duo** : A distance + B mêlée : le personnage au contact de Verminocule doit rester jusqu'au début de son tour ; si le placeur joue après Verminocule, impossible de corriger le tour de monstre déjà passé. Sur Pataugerme, les deux personnages doivent couvrir quatre éléments **réellement** via sorts/armes accessibles, sans les inventer.
- **Trio** : placeur + frappeur + soigneur : pour Protozorreur, préparer un glyphe (personnage blessé), aligner boss, faire la **première attaque du boss du tour** pour déclencher son attraction dans le glyphe. Si le soigneur crée un soin pendant la fenêtre Tabacille, il réinitialise son compteur malgré l'aide à l'équipe.
- **Quatuor** : protecteur + placeur + deux frappeurs : sécuriser le délai de Malamibe avant le tour ennemi concerné, distribuer les quatre éléments de Pataugerme, puis entretenir Infection du boss. Ordre favorable : mise en place avant frappe qui attire le boss ; ordre inverse : une frappe prématurée consomme le déclencheur une fois/tour et peut faire rater la fenêtre. Le compagnon compte pour le dimensionnement Malamibe selon S2, mais son éventuelle admissibilité au contact Verminocule reste à prouver.

## Contradictions, risques et reprise

1. **Ancien titre du tableau « cinq tours »** dans S2, malgré détails patchés à **trois** ; exploiter la règle précise du patch S1, pas le titre hérité.
2. **Bacterrib** : S2 « aucun adversaire à 5PO ou moins », S4 ancien « plus de 6PO » ; proposer une trace aux distances **5, 6 et 7 PO** et présence d'une invocation.
3. **Virustine** : distinction mouvement réussi, tentative de retrait PM même esquivée, et recul contre obstacle qui fait des dommages de poussée sans changement de case ; règles exactes de l'exception à confirmer.
4. **Conditions mondiales de Tabacille** : un soin automatique ou de compagnon pourrait casser la restauration ; ne pas généraliser « soin du soigneur » uniquement.
5. **Phase de mort** : UI compte à rebours insuffisante pour prouver la mort ; journal serveur requis.

**Livrables associés :** [FICHE_PROTOZORREUR_KROBES.md](./FICHE_PROTOZORREUR_KROBES.md) et [PASSAGE_DEVELOPPEUR.md](./PASSAGE_DEVELOPPEUR.md). Aucun test exécuté. **Reprise** : chercher vidéo authentifiée 2025–2026 de cette mécanique si disponible, utiliser MCP langue/transcript puis inspecter passage visuel ; relever replay 3.7.13.17 avec UID/cycle, condition par Krobe, évaluation/expiration, état Infection et naissance/mort des Malamibes.
