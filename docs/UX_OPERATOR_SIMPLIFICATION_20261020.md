# Nettoyage UX opérateur (20 octobre 2026)

Demande : « enlève les trucs slops, les différents interfaces qui servent à rien ; moins dense, plus lisible, plus user friendly ».

## Écrans principaux préservés

- **Comptes** : les outils MZP4 (comptes de test hors jeu) et MZP10 (vérification du mapping) ne précèdent plus l'ajout d'un compte. Ils sont regroupés en **Diagnostic avancé des connexions**, replié par défaut ; cela libère la première vue pour importer, ajouter et gérer les comptes. Aucun protocole/test n'est supprimé ni exécuté automatiquement.
- **Bots → Paquets** : le flux réel + inspecteur conservent leur place ; MZP4 (offline) et MZP6 (catalogue du protocole) sont dans **Outils de diagnostic du protocole**, repliés. Cela distingue clairement observation du trafic et données fictives/hors jeu.
- **Tableau de bord** : trois synthèses réellement distinctes (sessions, en jeu, attention) au lieu de répéter les chiffres des superviseurs. Labels compréhensibles, dimensions et marges homogènes, moins de micro-copies. La section Superviseurs conserve son vrai compteur, les alertes restent accessibles.
- **Atelier objets/panoplies** : le « grand hero » promotionnel et les quatre tuiles décoratives numérotées ont été supprimés ; une courte consigne, **Calculer mon plan** et **Voir un exemple** restent visibles. Suppression du `MinWidth=970`/scroll horizontal forcé ; le plan, les stocks, limites, inconnues, provenance et démo fictive restent accessibles.

## Principes respectés

Les interfaces de diagnostic n'ont pas été détruites : une fonction de sécurité ou une preuve locale peut servir au débogage. Le rangement dans des sections `Expander` est volontaire : on réduit l'encombrement sans réintroduire de menu caché permanent ou de perte de capacités. Les contrôles de licence, source, version, inconnu et validation serveur ne sont pas modifiés.

## Validation

Les quatre XAML concernés passent le parseur XML standard. Compilation Release de l'application nécessaire pour les contrôles Avalonia, puis un contrôle visuel dans l'application (si disponible) avant de conclure à une validation UX complète. Aucun client Dofus n'est requis pour vérifier les panneaux.
