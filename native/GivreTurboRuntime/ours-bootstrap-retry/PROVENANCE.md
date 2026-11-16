# Bootstrap natif avec reessai du crochet (2026-10-19)

- Source : AlpaGit/dofus3-native-host @ 010eaac (bootstrap/src/version_proxy.cpp) + correctif Givre : SetWindowsHookExW(WH_GETMESSAGE)
  est reessaye (jusqu'a 200 fois, 100 ms, fenetre re-detectee) au lieu d'abandonner a la 1re erreur (erreur 126 observee ~6 ms apres l'injection,
  puis plus aucun mod charge) ; apres 5 echecs, repli : sous-classement de la procedure de la fenetre Unity (meme rappel sur le fil Unity). Journal WARN dans native-bootstrap.log.
- Compilation : cl.exe (VS 2022 BuildTools) /std:c++20 /EHsc /O2 /W4 /WX /permissive- /guard:cf, memes options que build.ps1 du depot.
- SHA-256 : 9b9c5f96dbf762ea7fb9d5af395d4f22622781da0b2b53ffa25c20c6c50ea75e
- Remplace : 2c24fa8f9905427a5e637484402df492d1942ea2de1253a3e8b49ab7e6dfda3d (build precedent, 211968 octets)