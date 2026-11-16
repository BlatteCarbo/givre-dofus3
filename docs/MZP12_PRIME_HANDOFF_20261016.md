# MZP12 — liaison de la version au parcours Socket historique

## RESULT

Le vrai GivreClone.App reste compilable, mais pas encore un clone jouable vérifié. La version Dofus installée est 3.7.11.11 selon le manifeste Cytrus. Aucun login, personnage, carte, ACK ou combat authentifié n'a été observé dans cette passe. GivreRework G0 demeure NOT_COMPARED.

## CHANGES

- GivreClone.App/Services/MZP12SocketVersionBinding.cs : exige l'égalité exacte du build de mapping, de la version du manifeste Cytrus et de son SHA-256 au moment de la publication. Une réponse required_version différente est rejetée sans retraiter les anciens tags.
- GivreClone.App/Services/MappingCatalog.cs : attache le build et le digest Cytrus à un mapping effectivement publié, les purge sur Clear. Aucun bundle refusé n'obtient cette admission.
- GivreClone.App/Services/Transport/Socket/SocketProbe.cs : admission avant l'authentification, contrôle juste avant ouverture de socket et refus de retry sur un autre build.
- GivreClone.App/Services/MZP11CytrusVersionProbe.cs : cache vérifié par SHA des octets du GZIP original. Evite de décompresser les 80 Mo SQLite à chaque reconnexion si le SHA n'a pas changé. N'utilise jamais seulement le timestamp.
- GivreClone.App/Views/MZP10MappingVerificationPanel.cs : protection Socket affichée dans la véritable page Comptes.
- tools/MZP12SocketBindingHarness : tests adversariaux à partir des véritables classes App.
- tools/MZP11CytrusProbeHarness : test de revérification SHA et cache invalidé par de nouveaux octets.

## VALIDATION

MZP12 Socket version binding : **15 contrôles PASS hors ligne**. MZP11 Cytrus : **11 PASS** incluant le fichier installé, SHA 30117D42F6FAA1230985B58CB4865384E348C5FCCF93573AACAAA50A00B94C94. Panneau Comptes réel Avalonia Headless : **8 PASS**. Vrai projet App Windows x64 **Release : 0 erreur**, **Debug : 0 erreur / 144 avertissements** (Debug inclut le diagnostic data/ absent). Le dernier build Release incrémental a **0 avertissement** ; le projet conserve des avertissements historiques lors des rebuilds complets. Le paquet SQLite natif vulnérable n'est plus utilisé.

## BLOCKERS / NEXT

Les permissions/identités indépendantes du Launcher TEST, le handshake authentifié, le mapping d'octets inbound serveur exact 3.7.11.11 et le gameplay manquent toujours. La présence d'un binaire ou d'un SHA ne certifie aucune session. Ne toucher ni aux trois bots actifs du GivreClone original ni à ses fichiers de comptes, dumps ou jetons. Aucun mot de passe transmis dans le chat ne doit être utilisé pour les tests.

Prochaine priorité : instance TEST séparée, authentification officielle interactive et preuve corrélée serveur/personnage/carte/action/UID; puis vrais tests combat, carte, inventaire, banque, HDV et H24.
