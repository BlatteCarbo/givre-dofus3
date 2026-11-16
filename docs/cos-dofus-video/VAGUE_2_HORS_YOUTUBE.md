# Vague 2 CoS : preuves client, replays et performance

## Prompt pret a coller

Tu coordonnes une deuxieme vague pour GivreClone. La vague documentaire M01-M33
est livree dans `rapports/`. Ne la recommence pas. Lis sa synthese, ses limites et
les passages developpeur pertinents. L'objectif est maintenant de reduire les
inconnues avec des donnees verifiables, pas de produire davantage de conseils.

Racine du projet :
`%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`.
Client cible : DOFUS officiel 3.7.13.17, installation attendue
`%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3` ; verifier version et empreintes.

Travaille en parallele si tes outils le permettent. Chaque mission ecrit seulement
dans `docs/cos-dofus-video/rapports-v2/Nxx/`. Le coordinateur ecrit uniquement
`rapports-v2/INDEX.md`. Ne remplace pas les rapports de la premiere vague.
Lecture seule du code, des binaires, des journaux et des configurations existants.
Ne modifie ni compte, ni configuration, ni processus, ni session de jeu : une
session Bastionbot est reservee aux validations du developpeur. Pas d'action Git,
de lancement du jeu/bot, d'injection, de publication ou d'installation globale.
Outils temporaires et sorties dans ton dossier de mission ; conserve leur version.
Les outils disponibles font foi, un nom de MCP ne prouve pas une capacite.

Priorite : N01 et N02, puis N03-N06. N07-N10 sont independantes.
Livre une mission des qu'elle contient une preuve nouvelle exploitable.
Si une source manque, termine par un manque precis et la collecte minimale
necessaire ; ne transforme pas une absence de donnees en regle de jeu.

## Missions independantes

| ID | Travail borne | Preuve et critere d'arret |
| --- | --- | --- |
| N01 | Extraire les sorts et effets du client exact pour Sram et Roublard, puis Xelor/Eliotrope si le format est compris. Reutiliser les extracteurs existants avant d'en creer. | Donnees brutes JSON/CSV, build, SHA-256 des entrees, methode/outils, sort/rang/variante/action/masque/zone/condition/delai/duree/cout/portee. Trois lignes recoupees avec une source independante ou divergence explicite. Ne pas confondre ID de sort, effet et niveau. Arret : export reproductible d'une classe ou format/champ bloquant identifie. |
| N02 | Analyser les journaux locaux existants : tour vide du compagnon Prudent avec 11 PA/6 PM, sorts sans beneficiaire et marche PM par PM. | Une chronologie par incident : acteur controle, choix du planificateur, ordre envoye, resultat serveur, etat suivant. Chemin + lignes/offsets et temps bruts ; anonymiser les identifiants de compte. Classer cause prouvee, hypothese et champ absent. Aucun combat neuf. Arret : un incident explique ou collecte discriminante definie. |
| N03 | Mesurer hors jeu le cout du planificateur a partir de fixtures existantes, dans une copie de travail isolee, sans modifier le worktree actif. | Identifier d'abord les commandes et projets existants. Execution uniquement dans une sortie isolee et avec les outils deja disponibles ; sinon livrer le protocole. Version runtime, materiel, corpus, budget, noeuds, distributions de duree, allocation si disponible. Separer chargement/JIT et decision ; conserver les cas lents et decisions, sans promettre un gain non mesure. |
| N04 | Extraire des replays minimaux anonymises depuis des traces deja presentes, pour les confirmations/refus/positions et le changement d'acteur vers compagnon. | Sequence ordonnee, schema, etat initial/final, evenements absents explicites. Jeu de donnees et script de reproduction seulement dans N04 ; pas de nouvelle suite copiant les tests existants. Arret : un replay utilisable sans compte et un cas negatif distinct. |
| N05 | Auditer les donnees de marques Sram/Feca et entites Roublard dans le client et les captures existantes. | Relier type de marque, auteur, cases, sort/rang, activation et retrait avec preuves exactes. Lire METADATA-3.7.13.17.md et FightMarks avant toute conclusion. Un trigger ne vaut pas retrait ; la vie d'une bombe ne prouve pas une formule de mur. Arret : une transition prouvee et une inconnue precise. |
| N06 | Chercher les regles exactes de portails et Telefrags dans les artefacts et donnees actuels, plutot que les seules videos. | Isoler geometrie/ordre/egalites/fermeture d'une part, permutation/etats/remboursement d'autre part. Fournir provenance et contre-exemple qui departage deux interpretations. Pas d'invocation native ni formule inventee. Arret : une regle demontree ou le symbole/evenement manquant. |
| N07 | Comparer les donnees de sorts 3.7.12.12 et 3.7.13.17 disponibles localement ou publiquement. | Difference machine-lisible sur IDs stables, champs ajoutes/supprimes et valeurs changees ; URL/date/build et empreintes. Une difference de nom n'est pas une difference de mecanique. Arret : diff borne des quatre classes prioritaires ou version source inaccessible. |
| N08 | Construire des cas adverses de soutien : boost sans attaque atteignable, soin Insoignable, bouclier deja suffisant, beneficiaire jouant avant/apres le lanceur. | Lire les tests et le resolver actuels. Fournir uniquement les cas non deja couverts, entrees minimales et oracle independant. Les actions 115/118/138/418 viennent de recevoir un correctif de routage : ne pas refaire cette correction. Arret : trois lacunes reelles ou constat documente de couverture. |
| N09 | Audit lecture seule des parcours chasse et expedition : reprise apres mort, dialogue, inventaire plein, refus serveur et carte restrictive. | Suivre les appels reels et tests, citer fichier:ligne, decrire le scenario et l'invariant viole pour chaque finding. Aucune modification ni faux test live. Zero energie ne doit jamais bloquer ; aucun Phoenix dans le flux actuel. Arret : findings priorises avec preuves, ou aucune anomalie avec perimetre lu. |
| N10 | Reconstituer l'ordre d'activation Turbo depuis les logs existants : entree combat, premier tour ennemi, bail, multiplicateur effectif et retour hors combat. | Separer temps CPU, serveur, animations et chargement du module. Aucune equivalence Socket/MITM sans mesure visible comparable. Lire les validations existantes avant de conclure. Arret : chronologie d'un combat et instrumentation manquante exacte. |

## Outils utiles, sans configuration automatique

- Acces fichiers et recherche de code : obligatoire pour N02/N04/N08/N09/N10.
- Terminal local avec sorties et codes retour : extraction, hachage, diff et mesures.
- Navigateur ou HTTP en lecture seule : docs de formats et donnees publiques
  versionnees ; conserver URL et extrait, ne pas envoyer de journaux prives.
- Lecteur d'assets Unity / metadata IL2CPP : utile pour N01/N05/N06, apres
  inventaire des outils existants. Ne pas modifier les fichiers du client.
- Un parseur JSON/CSV suffit aux comparaisons ; aucune IA video necessaire.
- Aucun besoin de MCP de pilotage du compte, d'injection ou de messagerie.

## Contrat commun de livraison

Chaque Nxx livre `RAPPORT.md`, `PREUVES.md`, `PASSAGE_DEVELOPPEUR.md`, et ses
donnees brutes utiles. Rapports courts : resultat d'abord, methode reproductible,
version/empreintes, faits vs hypotheses, limites, integration fichier:ligne.
Les donnees sensibles restent locales et les copies sont expurgees. Ne livre pas
de credentials, jetons, cookies ou donnees personnelles dans les exemples.

Un resultat de test exige commande + code retour + sortie. Un benchmark exige
donnees brutes et protocole. Une regle exige une source primaire ou le statut
hypothese. Un scenario propose ne porte jamais le statut execute.
Le coordinateur recoupe les conclusions et indexe : mission, preuve nouvelle,
chemins, limite, prochaine action. Les matrices de 5 016 compositions restent un
index combinatoire, pas une base de synergies validees.
