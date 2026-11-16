# Récolte sans filtre — consigne utilisateur du 26 octobre

L'utilisateur demande que tous les scripts de récolte ramassent tout ce que les métiers permettent sur les cartes parcourues. Cette consigne remplace les anciennes restrictions « bois uniquement », « pêche uniquement » et listes de minerais.

Sept scripts sauvegardés mis à jour et relus via validation Lua : Mineur Minerbot, Mineur Bastionbot, Bûcheron Bastionbot, Pêcheur Bastionbot, Nuit Foragerbot avoine, Nuit Minerbot pêche, Nuit Bastionbot orties. Copies sources dans scripts/. Suppression des globals ELEMENTS_TO_GATHER, ITEMS_TO_GATHER et de la reconstruction dynamique du filtre de minerais au changement de niveau. Les sélections de circuits par niveau, protections de combat et banque restent en place.

Syntaxe valide pour les sept scripts. Sauvegardes antérieures locales dans artifacts/recolte-filters-before-20261026/. Aucun filtre restant détecté dans scripts/. Les noms historiques de scripts sont conservés mais ne limitent plus la ressource récoltée.

Bastionbot et Minerbot ont été arrêtés hors combat, arrêt relu puis scripts mis à jour relancés. État actif confirmé pour les deux après relance, trajets en cours. Foragerbot conserve son script de combat. Le mode sans filtre de gatherAll reste limité aux vraies ressources récoltables et aux compétences disponibles ; il ne demande pas de visiter d'autres cartes que le circuit.
