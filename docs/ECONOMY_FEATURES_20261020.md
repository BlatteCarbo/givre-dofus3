# Économie consultative — 20 octobre 2026

Cette passe n'envoie **aucun ordre** de marché ni de déplacement.

## Comparer une panoplie

Dans l'inventaire, choisir une panoplie enregistrée et cliquer **Prévisualiser les changements**. Le panneau compare les GID par emplacement, affiche le nombre de changements et les pièces non trouvées dans le sac. Un preset existant enregistre uniquement l'emplacement et le GID : il **ne contient pas les jets**, ni le prix, ni l'UID d'exemplaire. L'interface refuse donc de fabriquer des gains de statistiques.

Le moteur pur `GearComparison.Compare` accepte des instantanés détaillés avec valeurs et complétude des effets. Une différence numérique est produite uniquement lorsque **tous** les effets, des deux côtés, sont explicitement connus. Une valeur inconnue produit `Delta = null`, pas un zéro. Aucun accès au jeu/HDV n'est exécuté par le moteur.

## Budget de clés d'expédition

Dans le mode donjon, le nombre de passages multiplie les besoins par clé; les stocks observés du **sac du personnage sélectionné** sont déduits une seule fois, même lorsqu'une clé apparaît dans plusieurs étapes. Le coffre de banque n'est pas supposé consulté. Une session de personnage absente produit un état « non consulté » plutôt qu'un stock zéro inventé.

La bibliothèque pure `ExpeditionBudget.Estimate` accepte en plus des prix datés et des stocks en banque lorsqu'ils sont réellement fournis. Un prix absent/périmé rend le coût total `null` ; le sous-total ne compte que les lignes chiffrées connues. Avec des passages illimités (`runs=0`), le besoin est montré **par passage** et le total est inconnu.

## Tests

`dotnet test GivreClone.PureTests/GivreClone.PureTests.csproj -c Release`

Le module est préparatoire et ne remplace pas les validations de stock en temps réel ni les reçus serveur avant d'équiper/acheter/vendre.
