# Teleportation par clic droit sur un zaap de la carte du monde (capture MITM 31 oct)

Client 3.7.12.12, PID 10496, capture de decouverte. Journal: %LOCALAPPDATA%/GivreReplica/TestRuns/givre-app-offline/userdata/logs/transport-2026-10-31_18-35-32.log

Parcours officiel: carte du monde (X dore de la barre), clic droit sur l'icone de zaap, "Se teleporter (N kamas)". Aucun ouvrage du zaap physique, aucun havre-sac.

Trames C->S TeleportRequest [hgz], 45 octets, enveloppe uid -1:
- 21:29:53 refusee (zaap Frigost 3 non debloque): champ1=54172489, champ2=5. Chat: "Impossible de se teleporter vers cet endroit."
- 21:33:09 acceptee (Zaap Routes Rocailleuses -20,-20, 210 kamas): champ1=164364304, champ2=5. Puis interactifs: cell 271=Zaap [id 540327].
- Pour comparaison 21:31:59 via zaap physique: TeleportDestinationsEvent (type 3) puis TeleportRequest champ1=73400320, champ2=3.

Lecture: champ1 = map id de destination, champ2 = type de teleportation; 5 = depuis la carte du monde, 3 = zaap physique. Noms de champs a confirmer dans le mapping protocole existant. Le cout (210) n'apparait pas dans la requete: a verifier via l'evenement kamas.


## Zaapi (objet de ville, ex. La Bourgade) - capture 21:37
Interface "Zaapi - La Bourgade", onglets Ateliers / Hotels de vente / Divers, colonnes nom, quartier, coordonnees, cout (20 kamas), case favoris, bouton Se teleporter.
- 21:37:15 C->S InteractiveUseRequest [itz] champ1=28664 (element), champ3=524248 (skill), sur l'objet "Zaapi" de la carte du zaap.
- S->C InteractiveUsedEvent puis TeleportDestinationsEvent [hgq] type 1, 9 destinations (aujourd'hui ignore par le bot: "type 1 -> ignoree").
- 21:37:53 C->S TeleportRequest [hgz] 3 champs: champ1=54169896 (map Sortie Est -72,-40), champ2=1 (type zaapi), champ4=1 (a identifier dans le mapping, peut-etre le type source ou l'index). Puis DialogLeaveEvent, arrivee -72,-40, chat "Vous avez perdu 20 kamas".
- Zaap physique 21:37:07: TeleportRequest 1 champ (map 54172969), type 0 omis (proto3). TeleportDestinationsEvent type 0, 49 destinations.
Types observes: 0 zaap physique (liste type 0), 1 zaapi, 3 zaap (liste type 3), 5 depuis la carte du monde.
