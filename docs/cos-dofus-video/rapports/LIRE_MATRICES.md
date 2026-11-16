# Matrices de couverture des combinaisons

Ce dossier comporte un générateur reproductible, `GENERER_MATRICES.ps1`, qui indexe toutes les combinaisons **de classes distinctes** de taille 2, 3 et 4 parmi les 19 classes jouables. Exécuter dans ce dossier avec PowerShell 5.1+ : `.\GENERER_MATRICES.ps1`. Les fichiers CSV sont enregistrés avec `;` comme séparateur, pratique pour Excel en français ; régénération volontaire par `-Force`. Par défaut, les CSV existants sont préservés.

| Fichier | Lignes de données | Interprétation |
| --- | ---: | --- |
| [MATRICE_DUOS_171.csv](MATRICE_DUOS_171.csv) | 171 | Toutes les paires distinctes |
| [MATRICE_TRIOS_969.csv](MATRICE_TRIOS_969.csv) | 969 | Tous les trios distincts |
| [MATRICE_QUATUORS_3876.csv](MATRICE_QUATUORS_3876.csv) | 3 876 | Tous les quatuors distincts |

Total : **5 016 combinaisons de classes** (sans doublons de classe et sans niveau/voies/équipement/initiative). Cet inventaire **ne signifie pas 5 016 stratégies étudiées** : `indexee_non_etudiee` indique précisément qu'aucun conseil propre à la combinaison n'a encore été vérifié. `declare_video_non_valide` identifie un duo ou quatuor **mentionné par un créateur** et renvoie au chapitre ; `hypothese_a_tester` identifie un trio déduit dans notre cours, sans preuve de combat.

**Usage recommandé** : filtrer `statut` puis `priorite`, prendre les combinaisons avec une preuve concrète ou une interaction nouvelle, reconstituer l'initiative et un scénario négatif, relier le résultat à la fiche de classe, puis mettre à jour le registre de synthèse. Éviter de transformer le simple nombre de compositions en une promesse de compatibilité de toutes les équipes. Les équipes à deux classes identiques, les compagnons, PvP/PvM et 5–8 personnages nécessitent un autre inventaire si ces dimensions sont effectivement à comparer.

