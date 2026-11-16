# GivreWork — première amélioration UI/UX (20 octobre 2026)

## Périmètre livré

La barre de titre commune aux skins par défaut et classique propose **Rechercher · Ctrl+K**.
La palette ouverte affiche les destinations locales connues et une recherche insensible
à la casse / aux accents, par mots indépendants. Les résultats sont bornés à un catalogue
statique de neuf actions et les requêtes de recherche à 128 caractères. Les actions
recyclent les méthodes de navigation actuelles du `MainViewModel` : tableau de bord,
bots, comptes, équipes, proxys, réseau, paramètres, mon compte et carnet visuel.
Le carnet n'est qu'un affichage local ; l'accès ne déclenche aucune injection.

Ergonomie : `Ctrl+K` ouvre la palette ; `Entrée` depuis le champ recherche ouvre
le premier résultat ; `Tab` permet de rejoindre les boutons ; `Échap`, le bouton
Fermer et le clic à l'extérieur ferment sans action. L'état **Aucune page correspondante**
est explicite. Les labels de boutons et du champ fournissent un nom d'accessibilité.

Sécurité : le raccourci ne s'ouvre pas sur le splash, les gates de licence / version,
les modales de notification et le chargement de protocole ou des cartes. Le login et
le blocage contrôle referment la palette déjà ouverte. La recherche ne connaît ni
carte serveur, ni opcode, ni transaction, ni téléportation ; elle n'exécute ni Lua
ni commande MCP. Les bindings du shell historique et des pages existantes sont conservés.

## Vérifications

- `dotnet build GivreClone.slnx -c Release -m:1 --no-restore -v:q -clp:ErrorsOnly`
- `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1 --no-build --no-restore --filter FullyQualifiedName~QuickNavigationCatalogTests -v:q`
- `dotnet test GivreClone.Tests/GivreClone.Tests.csproj -c Release -m:1 --no-build --no-restore --blame-hang-timeout 45s -v:q`
- `dotnet test GivreClone.PureTests/GivreClone.PureTests.csproj -c Release --no-build --no-restore -v:q`
- `git diff --check`

Première passe après création : Release 0 erreur / 194 avertissements, catalogue 7/7,
suite principale 1753 réussis / 2 ignorés, tests purs 211/211. Un contrôle de
compilation final après gardes clavier a également réussi : 0 erreur / 194 avertissements.
Au dernier rejeu, 1753 tests principaux réussis / 2 ignorés, et 211 tests purs réussis.
Une passe intermédiaire a reproduit l'échec intermittent connu de
`MZP0PacketBusTests.Publish_commits_waiter_before_slow_subscriber_even_if_timer_fires`
(1752 PASS / 1 FAIL / 2 SKIP). Le test a réussi isolément puis dans la suite suivante :
**l'intermittence demeure non corrigée**. Ces tests couvrent la logique, pas une
validation d'écran au clavier ou lecteur d'écran sur un poste graphique.

## Ce qui reste volontairement hors de ce lot

- Recherche universelle de contenus Dofus (quêtes, objets, scripts) : exige des index
  vérifiés et une politique de provenance / droits.
- Raccourcis vers exécutions de bots ou achats HDV : non introduits ; garde-fous métier
  et preuves serveur inchangés.
- Dashboard 3.0, profils de routines et mode compact : prochains lots distincts.
- Contrôle visuel interactif sur Windows / NVDA : non exécuté dans cette passe.
