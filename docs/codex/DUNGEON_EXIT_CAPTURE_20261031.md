# Quitter un donjon (bouton rouge porte + fleche) - capture MITM 31 oct 21:41

Bouton dans la barre en haut a gauche, a droite de l'oeil "N combat sur cette carte". Visible dans chaque salle selon l'utilisateur; capture faite en premiere salle du donjon des Champs (Grange du Tournesol Affame, 7,-24).
- C->S DungeonExitRequest [kgy], 0 champ (36 octets).
- S->C DungeonExitResponse [kgz], 1 champ (a lire dans le mapping, probablement le resultat).
- Puis C->S ContextReadyRequest et MapInformationRequest (map 0x5C...), arrivee Astrub (Champs d'Astrub) 7,-24.
Aucune confirmation demandee par le client.
