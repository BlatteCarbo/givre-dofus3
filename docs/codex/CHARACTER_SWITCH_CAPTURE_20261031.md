# Changement direct de personnage (menu principal du jeu) - capture MITM 31 oct 21:43

Parcours: bouton marche/arret en haut a droite > "Menu principal" > fleche de "CHANGER DE PERSONNAGE" > liste (nom, classe, niveau) > choix > popup "Etes-vous sur de vouloir vous reconnecter directement sur X ?" > CHANGER. Idem fleche "CHANGER DE SERVEUR".
Sequence observee (Bastionbot -> Cadeau, meme compte, ~6 s, relais MITM conserve):
1. C->S ReloginRequest [knd], 0 champ.
2. S->C ReloginTokenEvent [kmz], 2 champs (jeton de reconnexion; ne pas journaliser sa valeur).
3. Le client ferme la connexion de jeu ("connexion de jeu fermee par le client"), le bot attend 60 s une nouvelle session.
4. Nouvelle connexion de jeu (relais: 34.247.205.111:5555 puis 54.73.181.64:5555).
5. C->S IdentificationRequest [knc] 2 champs: champ1 = jeton recu en 2, champ2 = "fr".
6. S->C AuthenticationTicketAcceptedEvent ... ServerSessionReadyEvent.
7. C->S CharacterListRequest; S->C CharacterListEvent (14 persos).
8. C->S CharacterSelectionRequest champ1 = id du perso choisi (Cadeau 33110425954) envoye immediatement par le client.
9. Chargement normal (CharacterSelectionEvent, InventoryContentEvent, ZaapKnownListEvent = 47 zaaps connus, ... CharacterLoadingCompleteEvent).
Consequence: un changement de perso (et sans doute de serveur) en MITM ne relance pas Dofus: ReloginRequest + jeton + nouvelle IdentificationRequest. En Socket le bot peut rejouer la meme sequence sans fermer le compte.


## Changement de serveur (21:44 Mikhal -> Tylezia, 21:47 Tylezia -> Mikhal)
Menu principal > fleche "CHANGER DE SERVEUR" > liste des serveurs ou le compte a des persos (le serveur courant n'y figure pas) > popup "Etes-vous sur de vouloir vous reconnecter directement sur le serveur X ?" > OUI. Depuis l'ecran de selection: meme liste, sans popup.
Sequence: ReloginRequest -> ReloginTokenEvent -> fermeture par le client -> nouvelle connexion (redirection d'IP) -> IdentificationRequest -> CharacterListRequest -> CharacterListEvent. Aucune CharacterSelectionRequest automatique: le client reste sur l'ecran de selection (choisir puis JOUER).
Serveur de Bastionbot et Cadeau: Mikhal. Tylezia: player.three, Jade-hors, Jade-hors-la-chatte, Iikrai.

## Defauts observes
1. 21:47:06 S->C AuthenticationTicketRefusedEvent puis popup client "Connexion refusee... trop grand delai": premiere IdentificationRequest vers 54.246.90.95 refusee, le client a reessaye 3,5 s plus tard via 54.73.181.64 et a reussi. Cause a etablir (latence relais lors de la double redirection ?).
2. 21:45:35 fausse alerte "CLIENT DOFUS BLOQUE AU CHARGEMENT" alors que le client est volontairement sur l'ecran de selection apres un changement de serveur.
3. 21:48:04 [turbo-native] reconciliation echec apres 3 essais: chargement du mod tactique natif (F8) refuse, DNM_Load returned -1, apres relogin.
4. Le mod d'ID d'objets decore la liste des serveurs ("Dakal [20664] Familier d'apparat") en confondant nom de serveur et nom d'objet.
5. L'interrupteur "Capture de decouverte" reste affiche Active apres la coupure automatique de 15 min.
