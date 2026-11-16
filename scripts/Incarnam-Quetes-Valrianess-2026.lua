-- Adaptation GivreClone 2026 du script utilisateur ; original non modifie.
-- Reference de controle : https://ganymede-app.com/guides/2
--[[
Incarnam : adaptation du script utilisateur pour GivreClone et Valrianess/Kourial.
Les phases sont controlees par les evenements du serveur. Aucun succes global promis.
Les interactions de scene et les etapes non confirmees provoquent un arret explicite.
Pas de notifications externes, vente automatique ni retrait bancaire.
]]

-- ========================== RÉGLAGES ==============================
KAMAS_CIBLE      = 0 -- mission quetes, pas de retrait de banque
MESSAGE_FIN = "Parcours Incarnam controle ; voir le bilan des quetes serveur."
MIN_LIFE_PERCENT = 55
REGEN_UP_TO_PERCENT = 100
MIN_MONSTERS     = 1
MAX_MONSTERS     = 3
OPEN_BAGS        = true
IGNORE_REQUEST_EXCHANGE = true
IGNORE_REQUEST_DUEL     = true
-- Webhook Discord : départ, phases bloquées, morts et récapitulatif final y sont envoyés ("" = désactivé)
DISCORD_WEBHOOK = ""

-- ========================== DONNÉES ===============================
local PANO = { -- gid, emplacement, zone de drop
    { gid = 2478, pos = 0, zone = "champs"    }, -- Amulette
    { gid = 2475, pos = 2, zone = "foret"     }, -- Anneau
    { gid = 2477, pos = 3, zone = "lac"       }, -- Ceinture
    { gid = 2476, pos = 5, zone = "paturages" }, -- Bottes
    { gid = 2474, pos = 6, zone = "cimetiere" }, -- Chapeau
    { gid = 2473, pos = 7, zone = "route"     }, -- Cape
}
-- objets à ne jamais vendre (quêtes, crafts, clés, équipements)
local GARDER = { [16517]=1,[16520]=1,[16509]=1,[8545]=1,[16540]=1,[30832]=1,[16999]=1,[28256]=1,[6765]=1,
    [289]=1,[421]=1,[519]=1,[385]=1,[1984]=1,[16524]=1,[1782]=1,[303]=1,[312]=1,[16663]=1 }

local Z = {}
Z.route = { {153878787,{"right","left"}},{153879299,{"right","left"}},{154010371,{"right","left"}},{153879811,{"right","left"}},
            {154010883,{"right"}},{153880323,{"right","left"}},{153880835,{"left"}} }
Z.champs = { {153878788,{"left","top"}},{154010372,{"right","left","top"}},{153878789,{"bottom","left"}},{154010884,{"right","top"}},
             {154010373,{"right","bottom","left","top"}},{154010885,{"right","bottom","left","top"}},{154010374,{"bottom","left"}},
             {154011397,{"right","top"}},{154010886,{"right","bottom","left"}},{154011398,{"right","bottom"}} }
Z.lac = { {153878528,{"bottom","left","top"}},{153878529,{"left","top"}},{154010112,{"right","bottom","left","top"}},{153878785,{"bottom","top"}},
          {154010113,{"right","top"}},{154010624,{"right","top"}},{154010369,{"bottom","left","top"}},{153878786,{"bottom","left"}},
          {154010881,{"right","bottom","top"}},{154010370,{"right","bottom","left"}},{154010882,{"right","bottom"}} }
Z.paturages = { {153879300,{"right","top"}},{153879812,{"right","left","top"}},{153879301,{"right","bottom"}},{153880324,{"right","left","top"}},
                {153879813,{"right","bottom","left"}},{153880836,{"left"}},{153880325,{"bottom","left"}} }
Z.foret = { {153879040,{"right","top"}},{153879552,{"left","top"}},{153879297,{"right","bottom","top"}},{153879809,{"right","bottom","left","top"}},
            {153879298,{"right","bottom"}},{153880321,{"left","top"}},{153879810,{"right","bottom","left"}},{153880322,{"bottom","left"}} }
Z.cimetiere = { {153880064,{"right","bottom"}},{153881089,{"right","bottom","left","top"}},{153880578,{"right","top"}},{153881601,{"left","top"}},
                {153881090,{"left","top"}},{153881088,{"right","bottom"}},{153881600,{"bottom","left"}} }
Z.mine = { {153357312,{"bottom"}},{153357314,{"right","top"}},{153358338,{"left","top"}},{153358336,{"bottom"}} }
Z.astrub = { {189792256,{"right","bottom"}},{189792768,{"right","bottom","left"}},{189792257,{"right","bottom","top"}},{189793280,{"bottom","left"}},
             {189792769,{"right","bottom","left","top"}},{189792258,{"right","bottom","top"}},{189793281,{"right","bottom","left","top"}},
             {189792770,{"right","bottom","left","top"}},{189792259,{"right","bottom","top"}},{189793282,{"right","bottom","left","top"}} }

local function route(name, kind)
    local r = {}
    for _, s in ipairs(Z[name]) do
        local step = { map = tostring(s[1]), path = s[2] }
        if kind == "gather" then step.gather = true elseif kind == "both" then step.gather = true step.fight = true else step.fight = true end
        r[#r+1] = step
    end
    return r
end

local REPLY_TEXTS={
[24750]="Se renseigner sur les matériaux.",
[24751]="En savoir plus.",
[24752]="Explorer les lieux.",
[24753]="S'approcher de l'atelier.",
[24754]="Se diriger vers la sortie.",
[24770]="Explorer les lieux.",
[24783]="Répondre que vous êtes prêt à faire l'expérience.",
[24784]="Écouter la suite.",
[24785]="Demander comment activer le pouvoir d'une idole.",
[24786]="Répondre que vous êtes prêt à prouver votre valeur.",
[24793]="Engager le combat contre les trois monstres.",
[24794]="Battre en retraite.",
[24848]="S'approcher du Tigrimas Céleste.",
[24849]="Écouter la suite.",
[24878]="Vérifier que l'idole est en place, puis s'approcher du tigrimas.",
[24879]="Vérifier que l'idole est en place.",
[24880]="Demander comment activer le pouvoir d'une idole.",
[24891]="Faire demi-tour et entrer dans le temple.",
[24892]="Le remercier et chercher Ternette Nhin.",
[24893]="Demander où trouver Ternette.",
[24894]="Répondre que vous n'avez pas besoin d'aide et partir.",
[24895]="Demander qui est Ternette.",
[24896]="Répondre que vous préférez apprendre par vous-même.",
[24897]="S'en aller.",
[24898]="Le remercier et chercher Ternette Nhin.",
[24899]="Demander où trouver Ternette.",
[24900]="Répondre que vous n'avez pas besoin d'aide et partir.",
[24901]="Demander qui est Ternette.",
[24902]="S'en aller.",
[24903]="Lui souhaiter une bonne retraite et s'en aller.",
[24904]="S'éloigner pour réfléchir.",
[24905]="Répondre que cela pourrait remettre en cause votre quête.",
[24906]="S'éloigner le temps qu'il se calme.",
[24907]="S'excuser.",
[24908]="Le saluer et s'en aller.",
[24909]="Reconnaître une certaine sagesse dans ses paroles.",
[24910]="Écouter la suite.",
[24911]="Soutenir son regard.",
[24912]="S'éloigner en rampant.",
[24913]="Le traiter de vieux gâteux.",
[24914]="Dire que vous ne croyez pas un mot de ce qu'il raconte.",
[24915]="Savoir ce qui n'a pas marché comme prévu.",
[24916]="Lui demander s'il a cherché les 6 Dofus.",
[24917]="Écouter la suite de ses exploits.",
[24918]="En savoir plus à son sujet.",
[24919]="Se présenter.",
[24920]="Suivre sa suggestion et faire un tour dans le village.",
[24921]="Prendre la direction des pâturages d'Incarnam.",
[24922]="Demander où se trouve Xélora.",
[24923]="Demander comment fonctionnent les zaaps.",
[24924]="Se renseigner sur le coût en kamas.",
[24925]="Demander ce que sont les zaaps.",
[24926]="Dire que vous êtes impatient d'explorer l'île céleste.",
[24927]="La remercier pour son accueil.",
[24928]="La remercier puis se diriger vers la bibliothèque.",
[24929]="Confirmer.",
[24930]="Annoncer que vous avez rendu visite à plusieurs habitants du village.",
[24931]="Prendre la direction des pâturages d'Incarnam.",
[24932]="Demander où se trouve Xélora.",
[24933]="Demander comment fonctionnent les zaaps.",
[24934]="Se renseigner sur le coût en kamas.",
[24935]="Demander ce que sont les zaaps.",
[24936]="Dire que vous êtes impatient d'explorer l'île céleste.",
[24937]="Partir à la recherche de cinq vestiges des dieux.",
[24938]="Écouter la suite.",
[24939]="En apprendre davantage à ce sujet.",
[24940]="Dire que vous avez parlé à Xélora.",
[24941]="Partir à la recherche de cinq vestiges des dieux.",
[24942]="Écouter la suite.",
[24943]="En apprendre davantage sur les dieux.",
[24944]="Chercher Galilea et Matu Vuh.",
[24945]="Répondre que vous avez hâte de découvrir ce monde.",
[24946]="L'interroger à propos du Monde des Douze.",
[24947]="Annoncer que vous avez trouvé cinq vestiges laissés par les dieux.",
[24948]="Chercher Galilea et Matu Vuh.",
[24949]="Répondre que vous avez hâte de découvrir ce monde.",
[24950]="L'interroger à propos du Monde des Douze.",
[24951]="La saluer et s'en aller.",
[24952]="Dire que vous avez observé le Monde des Douze.",
[24953]="Se placer devant l'oculaire.",
[24954]="Demander si vous pouvez jeter un coup d'œil.",
[24955]="S'approcher de la lunette.",
[24956]="Demander si vous pouvez jeter un coup d'œil.",
[24957]="Dire que c'est Ternette qui vous envoie.",
[24958]="S'approcher de la lunette.",
[24959]="Demander si vous pouvez jeter un coup d'œil.",
[24960]="Se placer devant l'oculaire.",
[24961]="Demander si vous pouvez utiliser la lunette.",
[24962]="S'approcher de la lunette.",
[24963]="Dire que c'est Ternette qui vous envoie.",
[24964]="S'approcher de la lunette.",
[24965]="Demander si vous pouvez utiliser la lunette.",
[24966]="Retourner dans cette salle.",
[24967]="Reprendre la Crypte de Kardorim où vous l'avez quittée.",
[24968]="Oui.",
[24969]="Non.",
[24970]="Donner la clef et entrer.",
[24971]="Oui.",
[24972]="Non.",
[24973]="Utiliser le trousseau de clefs.",
[24974]="Poser une autre question.",
[24975]="Le remercier pour cette information.",
[24976]="Se renseigner sur les clefs de donjon.",
[24977]="S'informer sur la recherche de groupe.",
[24978]="Obtenir des explications sur la sauvegarde en donjon.",
[24979]="Poser des questions sur le fonctionnement des donjons.",
[24980]="Téléporter les membres du groupe.",
[24981]="Poser une autre question.",
[24982]="Le remercier pour cette information.",
[24983]="Poser une autre question.",
[24984]="Le remercier pour cette information.",
[24985]="Écouter la suite.",
[24986]="S'en aller.",
[24987]="Demander comment pénétrer dans l'antre du Milimilou.",
[24988]="Écouter la suite.",
[24989]="Se renseigner sur le Milimilou.",
[24990]="Accéder à la crypte du Milimilou.",
[24991]="L'interroger à propos du Milimilou.",
[24992]="Écouter la suite.",
[24993]="En savoir plus sur lui.",
[24994]="Quitter la crypte de Kardorim.",
[24995]="Répondre que vous préférez attendre avant de déranger le Milimilou.",
[24996]="Accéder à la crypte du Milimilou.",
[24997]="Quitter la crypte de Kardorim.",
[24998]="Répondre que vous préférez attendre avant de déranger le Milimilou.",
[24999]="L'interroger à propos du Milimilou.",
[25000]="Quitter la crypte de Kardorim.",
[25001]="Quitter la crypte de Kardorim.",
[25002]="Attaquer le monstre.",
[25003]="S'enfuir de la crypte.",
[25004]="Attaquer le monstre.",
[25005]="Donner le pot de confiture au Milimilou.",
[25006]="Sortir un pot de confiture de votre sac.",
[25007]="Défendre chèrement sa vie.",
[25008]="S'enfuir de la crypte.",
[25009]="Sortir un pot de confiture de votre sac.",
[25010]="S'enfuir de la crypte.",
[25011]="Soupirer et partir.",
[25012]="Exiger qu'il vous remette ses Dofus.",
[25013]="Demander où est passé le Milimilou.",
[25014]="Quitter la crypte du Milimilou.",
[25015]="Accéder à la crypte du Milimilou.",
[25016]="Examiner le zaap juste à proximité.",
[25017]="Faire comme si vous aviez tout compris et la remercier pour ses explications.",
[25018]="Écouter la suite.",
[25019]="Lui demander comment elle peut réparer un zaap avec un marteau.",
[25020]="L'interroger sur l'origine des zaaps.",
[25021]="En savoir plus sur lui.",
[25022]="Se présenter.",
[25023]="Le saluer et s'en aller.",
[25024]="Se diriger vers la cave.",
[25025]="Dire que vous avez vu l'affiche placardée dehors.",
[25026]="Empocher la récompense et savourer la limonade.",
[25027]="Annoncer que vous avez neutralisé le rat.",
[25028]="En savoir plus sur lui.",
[25029]="Se présenter.",
[25030]="Se diriger vers la cave.",
[25031]="Dire que vous avez vu l'affiche placardée dehors.",
[25032]="Empocher la récompense et savourer la limonade.",
[25033]="Annoncer que vous avez neutralisé le rat.",
[25034]="Commander une limonade pour 1 kama.",
[25035]="Prendre un lailait bien frais pour 4 kamas.",
[25036]="Demander à boire.",
[25037]="Attaquer le rat.",
[25038]="Le saluer et s'en aller.",
[25039]="Accepter d'être mis à l'épreuve et se diriger vers l'escalier.",
[25040]="Renoncer à passer le test et partir.",
[25041]="Se renseigner sur le test d'aptitude.",
[25042]="Proposer vos services.",
[25043]="En savoir plus sur la milice.",
[25044]="Se présenter.",
[25045]="Accepter d'être mis à l'épreuve et se diriger vers l'escalier.",
[25046]="Renoncer à passer le test et partir.",
[25047]="Se renseigner sur le test d'aptitude.",
[25048]="Proposer vos services.",
[25049]="Partir chasser les esprits dans les champs.",
[25050]="Répondre que vous êtes prêt à servir la milice.",
[25051]="Renoncer et s'en aller.",
[25052]="Continuer à écouter.",
[25053]="Écouter ses instructions.",
[25054]="Annoncer que vous avez passé le test de la caporale Mynerve.",
[25055]="Dire que vous êtes disponible pour une mission.",
[25056]="Partir chasser les esprits dans les champs.",
[25057]="Répondre que vous êtes prêt à servir la milice.",
[25058]="Renoncer et s'en aller.",
[25059]="Continuer à écouter.",
[25060]="Suivre les ordres du capitaine.",
[25061]="Dire que vous avez rempli votre mission.",
[25062]="Partir chasser les esprits dans les pâturages.",
[25063]="Dire que les gloots ne seront plus un problème.",
[25064]="Suivre les ordres du capitaine.",
[25065]="Annoncer que vous avez accompli votre mission.",
[25066]="Partir dire deux mots à Kruella Freuz.",
[25067]="Dire que vous avez fait la chasse aux chapardams.",
[25068]="Partir en direction du cimetière.",
[25069]="Attendre ses instructions.",
[25070]="Écouter la suite.",
[25071]="Annoncer votre victoire sur l'Osamodas vaniteuse.",
[25072]="Le remercier et s'en aller.",
[25073]="Donner le sac de bijoux.",
[25074]="Le saluer et prendre la direction du cimetière.",
[25075]="Demander qui surveille le cimetière.",
[25076]="En savoir plus à ce sujet.",
[25077]="Lui demander s'il a aperçu le Dark Vlad depuis l'incendie sur l'Aire de l'Envol.",
[25078]="Le remercier et chercher la trappe.",
[25079]="Dire que vous avez besoin d'utiliser l'Orbalantyr.",
[25080]="Le remercier puis s'en aller.",
[25081]="Remettre la clef au capitaine.",
[25082]="La défier en combat singulier.",
[25083]="S'en aller.",
[25084]="Réprimer une furieuse envie de l'assommer et s'en aller.",
[25085]="Lui conseiller de faire profil bas si elle ne veut pas subir la colère de la milice.",
[25086]="Se défendre.",
[25087]="S'enfuir.",
[25088]="Commencer le combat.",
[25089]="Protester et insister pour montrer vos capacités.",
[25090]="Partir pleurer dans un coin.",
[25091]="Dire que vous êtes prêt à passer un test d'aptitude.",
[25092]="Commencer le combat.",
[25093]="Tenter à nouveau de passer le test.",
[25094]="Descendre voir le capitaine des Kerubims.",
[25095]="Demander si le test a été concluant.",
[25096]="La saluer et s'en aller.",
[25097]="Demander comment se procurer de tels matériaux.",
[25098]="Se présenter.",
[25099]="La remercier pour ces informations et s'en aller.",
[25100]="Écouter la suite.",
[25101]="Se renseigner sur les aventuriers marchands.",
[25102]="Chercher parmi les nombreux ouvrages.",
[25103]="Avouer une certaine ignorance.",
[25104]="Chercher parmi les nombreux ouvrages.",
[25105]="Répondre que vous connaissez cette légende par cœur.",
[25106]="Donner les noms des 6 œufs magiques et des dragons qui les ont pondus.",
[25107]="Répondre qu'elle a deviné juste.",
[25108]="Se présenter.",
[25109]="L'interroger à propos de la légende de Rykke Errel.",
[25110]="Chercher parmi les nombreux ouvrages.",
[25111]="Se mettre en quête des trois individus évoqués par Fécaline.",
[25112]="En savoir davantage.",
[25113]="Dire que vous avez terminé votre lecture.",
[25114]="La remercier pour ces informations.",
[25115]="Dire que vous êtes prêt à aller vérifier.",
[25116]="Continuer à écouter.",
[25117]="Écouter la suite.",
[25118]="Dire que vous avez parlé aux trois chercheurs de Dofus.",
[25119]="Aller trouver Maître Anemo.",
[25120]="Décider de parler à Anemo plus tard et rester sur Incarnam pour l'instant.",
[25121]="Aller trouver Maître Anemo.",
[25122]="Décider de parler à Anemo plus tard et rester sur Incarnam pour l'instant.",
[25123]="Répondre que vous n'êtes pas du genre à abandonner facilement.",
[25124]="Annoncer que vous avez retrouvé Hargnok, mais qu'il n'avait plus ses Dofus.",
[25125]="Prendre les dés.",
[25126]="Écouter la suite.",
[25127]="Lui demander si elle a cherché les 6 Dofus.",
[25128]="En savoir plus à son sujet.",
[25129]="La saluer et s'en aller.",
[25130]="Jeter les dés sur la table.",
[25131]="La saluer et s'en aller.",
[25132]="Jeter les dés sur la table.",
[25133]="La saluer et s'en aller.",
[25134]="Jeter les dés sur la table.",
[25135]="La saluer et s'en aller.",
[25136]="Jeter les dés sur la table.",
[25137]="Proposer de jeter les dés pour qu'elle vous dise ce qui vous attend.",
[25138]="La saluer et s'en aller.",
[25139]="Jeter les dés sur la table.",
[25140]="La saluer et s'en aller.",
[25141]="Jeter les dés sur la table.",
[25142]="La saluer et s'en aller.",
[25143]="Jeter les dés sur la table.",
[25144]="La saluer et s'en aller.",
[25145]="Jeter les dés sur la table.",
[25146]="Rester et se battre contre le feu maléfique.",
[25147]="S'enfuir.",
[25148]="Renifler l'air autour de vous.",
[25149]="Écouter la suite.",
[25150]="En savoir plus sur lui.",
[25151]="Lui demander ce qui lui est arrivé.",
[25152]="Le saluer et s'en aller.",
[25153]="En savoir plus sur le phénix.",
[25154]="Le saluer et partir en surveillant vos arrières.",
[25155]="Écouter la suite.",
[25156]="En savoir plus sur la mort et ses conséquences.",
[25212]="Consulter la recette.",
[25213]="Proposer de faire la galette à sa place.",
[25214]="Dire que vous appréciez mettre la main à la pâte.",
[25215]="Répondre que vous détestez jouer avec la nourriture et partir.",
[25216]="Demander si elle a du travail pour vous.",
[25217]="Aller voir Pipelette.",
[25218]="Montrer fièrement la galette.",
[25219]="Partir, puis cracher l'infâme mixture derrière un buisson.",
[25220]="Constater que la confiture a la saveur d'un orteil de trooll rance.",
[25221]="Goûter la confiture.",
[25222]="S'en aller.",
[25223]="Ne pas toucher à la confiture.",
[25224]="Donner la galette et le pot de confiture.",
[25225]="Partir cueillir des orties.",
[25226]="La remercier et s'en aller.",
[25227]="Donner des orties.",
[25228]="Lui demander un pot de confiture.",
[25229]="En savoir plus sur elle.",
[25233]="Procéder à l'échange.",
[25234]="Échanger une ficelle de vigne.",
[25290]="Dire que cela a été un plaisir de le rencontrer.",
[25292]="Se mettre au travail.",
[25293]="S'informer sur la fabrication des produits demandés.",
[25294]="Se renseigner sur les zones de récolte.",
[25295]="Dire que vous êtes prêt à prouver votre valeur.",
[25296]="Répondre que vous n'êtes pas intéressé.",
[25297]="Lui demander comment devenir un Fils d'Incarnam.",
[25298]="En savoir plus sur lui.",
[25299]="Le saluer et s'en aller.",
[25301]="Se mettre au travail.",
[25302]="S'informer sur la fabrication des produits demandés.",
[25303]="Se renseigner sur les zones de récolte.",
[25304]="Dire que vous êtes prêt à prouver votre valeur.",
[25305]="Répondre que vous n'êtes pas intéressé.",
[25306]="Lui demander comment devenir un Fils d'Incarnam.",
[25307]="Se mettre au travail.",
[25308]="Se renseigner sur les ingrédients.",
[25309]="Dire que vous avez terminé.",
[25310]="Continuer votre tâche.",
[25311]="Montrer les produits que vous avez confectionnés.",
[25312]="Dire que vous êtes prêt pour une autre tâche.",
[25313]="Se mettre au travail.",
[25314]="Se renseigner sur les ingrédients.",
[25315]="Se mettre au travail.",
[25316]="L'interroger à propos de l'hôtel des ventes.",
[25317]="En savoir plus sur les ingrédients nécessaires.",
[25318]="Se renseigner sur la panoplie.",
[25319]="Dire que vous avez terminé.",
[25320]="Montrer ce que vous avez fabriqué.",
[25321]="Continuer votre tâche.",
[25322]="Dire que vous avez terminé votre travail.",
[25323]="Se mettre au travail.",
[25324]="L'interroger à propos de l'hôtel des ventes.",
[25325]="En savoir plus sur les ingrédients nécessaires.",
[25326]="Se renseigner sur la panoplie.",
[25327]="Écouter la suite.",
[25328]="Dire que vous avez terminé.",
[25329]="Continuer votre tâche.",
[25330]="Montrer ce que vous avez fabriqué.",
[25331]="Se mettre au travail.",
[25332]="Continuer à écouter.",
[25333]="Dire que vous êtes prêt pour une autre tâche.",
[25334]="Se mettre au travail.",
[25335]="Continuer à écouter.",
[25336]="Écouter la suite.",
[25337]="Montrer ce que vous avez fabriqué.",
[25338]="Le remercier et s'en aller.",
[25339]="Dire que vous avez terminé.",
[25340]="Le remercier et s'en aller.",
[25341]="Dire que vous avez terminé.",
[25342]="Continuer votre tâche.",
[25485]="Prendre la direction des mines.",
[25486]="Proposer votre aide.",
[25487]="En savoir plus à son sujet.",
[25488]="Le saluer et s'en aller.",
[25489]="Donner la poudre d'Aminite.",
[25490]="Sursauter en voyant apparaître Ganymède.",
[25729]="Prendre le trousseau de clefs.",
[25737]="Oui.",
[25738]="Non.",
[25739]="Utiliser le trousseau de clefs.",
[25749]="Utiliser l'idole et engager le combat contre les deux monstres.",
[27056]="Engager le combat contre les trois monstres.",
[27061]="Patienter le temps que vos blessures disparaissent.",
[34465]="Le remercier et suivre son conseil.",
[34466]="Pousser un soupir de déception.",
[34467]="Écouter la suite.",
[34468]="Demander s'il n'existe pas un moyen de forcer le passage.",
[34469]="Dire que vous avez l'intention de franchir la Blessure d'Erazal pour entrer en Externam.",
[36612]="Se diriger vers l'une des créatures pour la provoquer.",
[36613]="Répondre que vous voulez partir explorer le Monde des Douze.",
[36614]="S'approcher du portail.",
[36615]="Décider de rester sur Incarnam.",
[36616]="Insister pour aller à Astrub.",
[36617]="Changer d'avis et rester sur Incarnam.",
[36618]="Insister pour aller à Astrub.",
[36619]="Changer d'avis et rester sur Incarnam.",
[36620]="Dire que vous voulez découvrir de nouveaux horizons.",
[36621]="S'approcher du portail.",
[36622]="Changer d'avis et rester sur Incarnam.",
[36740]="La remercier et s'en aller.",
[36741]="Décider de suivre sa suggestion.",
[36742]="S'en aller.",
[36743]="Décliner sa proposition.",
[36744]="En apprendre davantage sur Astrub.",
[36745]="Répondre que vous êtes fin prêt.",
[36746]="Dire que vous êtes impatient de montrer votre dévotion.",
[36747]="L'interroger à propos du culte de votre divinité.",
[36748]="Demander une téléportation vers votre temple.",
[36783]="Annoncer votre retour du temple.",
[36814]="La remercier et s'en aller.",
[36815]="Écouter la suite.",
[36974]="S'approcher du portail.",
[36975]="S'approcher du portail.",
[36976]="Parler à Maître Anemo avant de passer le portail.",
[36977]="Décider de partir pour Astrub malgré votre inexpérience.",
[36978]="Rester sur Incarnam.",
[36979]="Hésiter avant de quitter Incarnam, vous avez encore beaucoup à apprendre.",
[36980]="Confirmer votre décision de partir pour Astrub.",
[36981]="Rester sur Incarnam.",
[36982]="Hésiter avant de quitter Incarnam.",
[38436]="S'en aller.",
[38437]="Dire que le maître du temple s'appelle Ganymède, et non Joris.",
[39978]="Le remercier pour cette information.",
[39979]="Dire que vous cherchez à en savoir plus sur les phénix.",
[40290]="Lever votre verre et saluer Lykhen avant de quitter la taverne.",
[40291]="Solliciter un ultime conseil.",
[40292]="Dire que vous avez plusieurs voies qui s'offrent à vous.",
[40293]="Répondre que votre quête n'est pas tout à fait terminée.",
[40294]="Trinquer avec un verre de limonade.",
[40295]="Boire votre verre et saluer Lykhen avant de quitter la taverne.",
[40296]="Lever votre verre et saluer Lykhen avant de quitter la taverne.",
[40297]="Solliciter un ultime conseil.",
[40298]="Dire que vous avez plusieurs voies qui s'offrent à vous.",
[40299]="Répondre que votre quête n'est pas tout à fait terminée.",
[40300]="Boire votre verre et saluer Lykhen avant de quitter la taverne.",
[40301]="Trinquer avec un verre de lailait.",
[69013]="La remercier et s'en aller.",
[69014]="Décider de suivre sa suggestion.",
[69015]="S'en aller.",
[69016]="Décliner sa proposition.",
[69017]="En apprendre davantage sur Astrub.",
[69018]="Répondre que vous êtes fin prêt.",
[69019]="Dire que vous êtes impatient de rejoindre le lieu où subsiste votre héritage.",
[69020]="L'interroger à propos de la Lance Originelle.",
[69021]="Demander une téléportation vers votre temple.",
[69022]="La remercier et s'en aller.",
[69023]="Écouter la suite.",
[69024]="Annoncer votre retour du temple.",
[70793]="Répondre qu'il n'est de toute façon pas possible de faire marche arrière.",
[70794]="La remercier pour sa sollicitude et répondre que tout va pour le mieux.",
[70795]="Dire qu'à la réflexion, votre puissance tutélaire ne vous correspond pas.",
[70796]="Prendre le philtre.",
[70797]="Fouiller dans votre sac à la recherche du philtre.",
[70798]="Dire qu'à la réflexion, votre puissance tutélaire ne vous correspond pas.",
[70799]="Partir avec un regain de motivation.",
[70800]="Dire qu'à la réflexion, votre puissance tutélaire ne vous correspond pas.",
[77736]="Trépigner d'impatience.",
[77737]="Fouiller son sac à la recherche des objets et les équiper.",
[77738]="Se diriger vers le poutch céleste pour l'attaquer.",
[77739]="Le remercier pour cette information.",
[77740]="Dire que vous cherchez à en savoir plus sur les phénix.",
[77741]="Se concentrer sur les Dofus.",
[77742]="Lui demander ce qu'il entend par là.",
[77743]="Solliciter son avis sur votre situation.",
[77744]="Avouer ne plus trop savoir où vous en êtes.",
[77745]="Faire un résumé des derniers événements.",
[77746]="Se diriger vers l'arche de la salle des métiers.",
[77747]="Répondre que vous êtes partant.",
[77748]="Se diriger vers l'arche de la salle de combat.",
[77749]="Répondre que vous n'avez rien contre un peu d'exercice.",
[77750]="Se sentir confiant quant à l'avenir.",
[77751]="Inspecter les six statues de Dofus.",
[77752]="Se diriger vers la sortie du temple céleste.",
[77753]="Prendre le trousseau de clefs.",
[77754]="Remercier Ganymède pour son aide.",
[77755]="Se diriger vers la sortie du temple céleste.",
[77756]="Remercier Ganymède pour son aide.",
[77757]="Se diriger vers l'une des créatures pour la provoquer.",
[77760]="Rester sur vos gardes.",
[77761]="Dire que vous avez terminé.",
[77762]="Montrer les produits que vous avez confectionnés.",
[77764]="Dire que vous êtes prêt pour une autre tâche.",
[77770]="Monter le bouillon de chair.",
[77772]="Prendre le couteau de chasse et se mettre au travail.",
[77773]="Lui demander où obtenir une arme de chasse.",
[77774]="Prendre le couteau de chasse et se mettre au travail.",
[77775]="Lui demander où obtenir une arme de chasse.",
[77776]="Se mettre au travail.",
[77777]="Se renseigner sur les ingrédients.",
[77796]="Montrer ce que vous avez fabriqué.",
[77797]="Continuer à écouter.",
[77798]="Écouter la suite.",
[77799]="Dire que vous avez terminé.",
[77800]="Se mettre au travail.",
[77801]="Se mettre au travail.",
[77802]="Continuer à écouter.",
[77803]="Écouter la suite.",
[77804]="Dire que vous êtes prêt pour une autre tâche.",
[77869]="Suivre Ganymède.",
[77870]="Prendre note de cette information.",
[77876]="Continuer votre tâche.",
[77887]="Continuer votre tâche.",
[83103]="Rester pour s'entraîner.",
[83104]="Rejoindre Maître Hoboulo.",
[83190]="L'ignorer et sortir du Temple.",
[83191]="Partir avec un regain de motivation.",
[83192]="Dire qu'à la réflexion, votre puissance tutélaire ne vous correspond pas.",
[83193]="Se rendre dans la pièce suivante.",
[83200]="Consulter le livre du Temple.",
}
-- ======================== MINI-BIBLIOTHÈQUE ========================
local GOOD = {"se défendre","se battre","combattre","affronter","attaquer","prêt à prouver","prêt pour une autre","se mettre au travail","montrer","monter le bouillon","donner","rendre","remettre","ramener",
    "dire que vous avez terminé","avez terminé","annoncer","accepter","proposer","continuer votre tâche","couter la suite","continuer à écouter",
    "comment devenir","se présenter","demander","se renseigner","s'informer","interroger","en savoir plus"}
local BAD = {"pas intéressé","s'en aller","saluer","détestez","goûter","refuser","plus tard","au revoir","ignorer","s'enfuir","à boire","lailait","limonade pour","donner la clef","trousseau","reprendre la crypte"}

function talk(npcId, prefs, label)
    label = label or ("PNJ" .. tostring(npcId))
    -- A map arrival can precede publication of its NPC list. Wait for this
    -- exact template on the current map; never substitute the first NPC.
    local present=false
    for _=1,30 do
        for _,v in ipairs(map:getNpcs()) do
            if v.npcId==npcId or v.id==npcId then present=true break end
        end
        if present then break end
        delay(100)
    end
    if not present then printWarning(label .. " absent apres synchronisation de la carte") return false end
    if not npc:npc(npcId,3) then printWarning(label .. " introuvable sur la carte") return false end
    prefs = prefs or ({[2892]={24896,24901,24895,24899,24893,24898,24892},[1515]={25108,25107,25103,25102,25104,25106,25109,25110,25113,25118,25124,25123,25114,25115,25116,25117,25112,25111},[2905]={24919,24920,24930,24929,24928,24927,24935,24936,24925,24926,24921}})[npcId]
    local used = {}
    local sent=false
    for _ = 1, 20 do
        local q = npc:question()
        if not q then break end
        local ids, txts = q.replies or {}, q.replyTexts or {}
        for i,id in ipairs(ids) do txts[i]=txts[i] or REPLY_TEXTS[id] or "" end
        print(label .. " : " .. string.sub(tostring(q.text), 1, 90))
        local chosen
        for _, p in ipairs(prefs or {}) do
            if npc:hasReply(p) and not used[p] then chosen = p break end
        end
        if not chosen then
            local best, firstOk = 999, nil
            for i, id in ipairs(ids) do
                if not used[id] then
                    local t = string.lower(tostring(txts[i]))
                    local bad = false
                    for _, b in ipairs(BAD) do if string.find(t, b, 1, true) then bad = true end end
                    if not bad then
                        firstOk = firstOk or id
                        for k, g in ipairs(GOOD) do
                            if k < best and string.find(t, g, 1, true) then chosen, best = id, k end
                        end
                    end
                end
            end
            -- Aucun choix positionnel lorsque le sens de la reponse est inconnu.
        end
        if not chosen then printWarning("DIALOGUE NON RESOLU "..label.." question="..tostring(q.messageId)) break end
        used[chosen] = true
        print("   -> " .. chosen)
        sent=true
        if not npc:reply(chosen) then break end
    end
    npc:leaveDialog()
    delay(800)
    return sent
end

local function waitOpen()
    for _ = 1, 15 do if craft:isOpen() then return true end delay(200) end
    return craft:isOpen()
end
function craftAt(mapId, skillId, recipe, label)
    if not travelTo(mapId) then printWarning("trajet atelier raté") return false end
    local el
    -- L'arrivee peut preceder la liste d'interactifs, comme pour les PNJ.
    for _=1,30 do
        for _, it in ipairs(map:getInteractives()) do
            for _, s in ipairs(it.skills or {}) do if s.id == skillId then el = it.elementId end end
        end
        if el then break end
        delay(100)
    end
    if not el then printWarning("aucun atelier (skill " .. skillId .. ") ici") return false end
    for _ = 1, 3 do
        map:useById(el, skillId)
        if waitOpen() then break end
        leaveDialog() delay(800)
    end
    if not craft:isOpen() then printWarning((label or "craft") .. " : atelier non ouvert") return false end
    for _, r in ipairs(recipe) do
        if not craft:putItem(r[1], r[2]) then leaveDialog() return false end
    end
    local res = craft:ready()
    leaveDialog()
    delay(500)
    print((label or "craft") .. " = " .. tostring(res))
    return res
end

-- essaie PNJ puis interactifs de la carte jusqu'à ce que check() soit vrai
local SKIPNAMES = { "zaap", "hôtel de vente", "atelier", "four", "plan de travail", "établi", "scie", "enclume", "alambic", "moule", "puits",
    "blé", "ortie", "frêne", "goujon", "fer", "rékloot", "portes du temple", "indiquer une sortie" }
function solveMap(mapId, check, label, opts)
    opts = opts or {}
    if check() then return true end
    if mapId and not go(mapId) then return false end
    if not opts.noNpc then
        for _, p in ipairs(map:getNpcs()) do
            if check() then return true end
            talk(p.npcId or p.id, opts.prefs, (label or "PNJ") .. " " .. tostring(p.name))
            if mapId and getMapId() ~= mapId then travelTo(mapId) end
        end
    end
    if opts.noUse then return check() end
    local list = {}
    for _, it in ipairs(map:getInteractives()) do
        local n = string.lower(tostring(it.name))
        local skip = (it.resource and true or false) or #(it.skills or {}) == 0
        for _, s in ipairs(SKIPNAMES) do if string.find(n, s, 1, true) then skip = true end end
        if not skip then list[#list+1] = it end
    end
    for _, it in ipairs(list) do
        if check() then return true end
        if mapId and getMapId() ~= mapId and not travelTo(mapId) then return check() end
        local _, why = map:useById(it.elementId, -1)
        delay(1500)
        if not isInFight() then leaveDialog() end
        delay(500)
        print((label or "USE") .. " " .. tostring(it.name) .. " #" .. it.elementId .. " = " .. tostring(why) .. " -> " .. tostring(check()))
        if isInFight() then return check() end
    end
    return check()
end

-- trajet qui ne fait rien si on est déjà sur la carte (utile en donjon, hors du graphe)
function go(mapId)
    if getMapId()==mapId then return true end
    -- Annexe de la taverne : la cellule 313 est une interaction, pas une
    -- sortie par marche. Passage confirme vers la salle d'Aleha.
    if getMapId()==153358340 then
        leaveDialog()
        if not map:useById(488963,184) then return false end
        for _=1,30 do if getMapId()==153357316 then break end delay(100) end
        if getMapId()~=153357316 then return false end
        if mapId==153357316 then return true end
    end
    local temple={[241443329]=true,[241444353]=true,[241445377]=true}
    if temple[mapId] and not temple[getMapId()] then
        if getMapId()~=154010883 and not travelTo(154010883) then return false end
        leaveDialog()
        if not goUseInteractive(369) then return false end
        delay(1000)
    end
    if temple[getMapId()] then
        leaveDialog()
        map:sendMapRequest()
        for _=1,3 do
            local cur=getMapId()
            if cur==mapId then return true end
            if not temple[cur] then break end
            if not temple[mapId] and cur==241445377 then
                if not goUseInteractive(475) then return false end
            else
                local dir=(temple[mapId] and cur>mapId) and "left" or "right"
                if not changeMap(dir) then return false end
            end
            leaveDialog()
        end
        if temple[mapId] then return getMapId()==mapId end
    end
    return getMapId()==mapId or travelTo(mapId)
end
-- message Discord (webhook) ; ne bloque jamais le script
function discord(msg) end -- notifications externes desactivees
function has(gid, n) return getInventoryItemCount(gid) >= (n or 1) end
function cnt(gid) return getInventoryItemCount(gid) end
function qdone(q) return quest:isFinished(q) end
function qactive(q) return quest:isActive(q) end
function odone(q, o) return qdone(q) or quest:questObjectiveDone(q, o) end
tries = {}
function try(key, max)
    tries[key] = (tries[key] or 0) + 1
    if tries[key] > (max or 3) then printError("ABANDON " .. key .. " après " .. (max or 3) .. " essais") return false end
    return true
end
function npcStep(key, mapId, npcId, label, prefs)
    if not try(key, 4) then return false end
    if not go(mapId) then
        tries[key] = tries[key] - 1
        tries["trajet_" .. key] = (tries["trajet_" .. key] or 0) + 1
        if tries["trajet_" .. key] > 10 then printError("ABANDON " .. key .. " : trajet impossible vers " .. mapId) return false end
        delay(2000) return true
    end
    return talk(npcId, prefs, label)
end
local function waitFight() for _ = 1, 300 do if not isInFight() then break end delay(1000) end end

-- chasse ciblée : liste {objectif, monstre}, zone
local function hunt(q, list, zone)
    local f = {}
    for _, e in ipairs(list) do if not odone(q, e[1]) then f[#f+1] = e[2] end end
    if #f == 0 then return nil end
    FORCE_MONSTERS = f
    ITEMS_TO_GATHER = {}
    return route(zone, "fight")
end
local function farm(zone, force)
    FORCE_MONSTERS = force or {}
    ITEMS_TO_GATHER = {}
    return route(zone, "fight")
end
local function gatherZ(zone, items)
    FORCE_MONSTERS = {}
    ITEMS_TO_GATHER = items
    return route(zone, "gather")
end

-- IA native : sorts appris, couts/portees et reponses du serveur.
-- Pas de hook onTurn : un seul moteur est proprietaire du tour.
local ROT={}
function buildRotation() return ROT end
-- ===================== ENTRETIEN DU PERSONNAGE =====================
local lastHK = 0
local function statElement() return "chance" end
function equipPano()
    for _, p in ipairs(PANO) do
        if has(p.gid) and inventory:itemPosition(p.gid) == 63 and character:level() >= (inventory:itemLevel(p.gid) or 1) then
            local ok = inventory:equip(p.gid, p.pos)
            print("Équipe " .. getItemName(p.gid) .. " = " .. tostring(ok))
        end
    end
end
local function panoComplete()
    for _, p in ipairs(PANO) do
        local pos = inventory:itemPosition(p.gid)
        if pos == -1 or pos == 63 then return false end
    end
    return true
end
local function housekeeping()
    if os.time() - lastHK < 30 then return end
    lastHK = os.time()
    local lvl = character:level()
    if lvl < 6 then MAX_MONSTERS = 2 elseif lvl < 12 then MAX_MONSTERS = 3 elseif lvl < 20 then MAX_MONSTERS = 5 else MAX_MONSTERS = 8 end
    -- Points de caracteristiques geres par le profil natif Chance.
    -- Equipement limite aux pieces de panoplie explicitement presentes.
    pcall(equipPano)
    if achievement:hasReward() then
        local n = achievement:claimAllRewards()
        if n > 0 then printSuccess(n .. " récompense(s) de succès récupérée(s)") end
    end
end

-- ============================ PHASES ===============================
local P = {}

-- 0. Tutoriel : on le quitte (bouton « Quitter le tutoriel »)
P[#P+1] = { name = "Quitter le tutoriel",
    done = function() return map:currentSubAreaId()~=536 end,
    run = function()
        printError("Tutoriel guide actif : sortie non prise en charge par ce runtime")
        return false
    end }

-- 1. Réponses à tout (Temple Céleste)
P[#P+1] = { name = "Réponses à tout",
    done = function() return qdone(1631) end,
    run = function()
        if not qactive(1631) then
            if not npcStep("s1631", 241443329, 7581, "GANYMEDE", {77751,77741,83193,83200}) then return false end
            if not qactive(1631) then npcStep("s1631b", 241444353, 7581, "GANYMEDE", {83200,77754,77756,77752,77755}) end
            return
        end
        for _,step in ipairs({{9738,241443329,"DOFUS"},{9739,241444353,"LIVRE"}}) do
            if not odone(1631,step[1]) then
                if not go(step[2]) then return false end
                solveMap(nil,function() return odone(1631,step[1]) end,step[3],{noNpc=true})
                if not odone(1631,step[1]) and not quest:validateObjective(1631,step[1],5000,true) then
                    printError("Interaction de scene non confirmee : "..step[3].." objectif="..step[1])
                    return false
                end
            end
        end
        if not npcStep("e1631", 241444353, 7581, "GANYMEDE", {77754,77756,77752,77755,83200}) then return false end
    end }

-- 2. Le village dans les nuages
P[#P+1] = { name = "Le village dans les nuages",
    done = function() return qdone(1632) end,
    run = function()
        if not qactive(1632) then if not npcStep("s1632", 154010883, 2892, "LYKHEN") then return false end return end
        local steps = { {9762,154010371,2905,"TERNETTE"}, {9763,153878787,2899,"BERB"}, {9764,153357316,2885,"GROBID"},
                        {9765,153356294,2880,"CAPITAINE",{25044,25043}}, {9766,153879299,2910,"HOLLIE"}, {9767,154010371,2905,"TERNETTE"}, {9768,153356296,1515,"FECALINE"} }
        local current={}
        for _,o in ipairs(quest:objectives(1632)) do current[o.id]=not o.reached end
        for _,s in ipairs(steps) do
            if current[s[1]] then
                local spoken=npcStep("v"..s[1],s[2],s[3],s[4],s[5])
                if not spoken and not odone(1632,s[1]) then return false end
                return
            end
        end
        quest:refresh(1632)

    end }

-- 3. Espoirs et tragédies
P[#P+1] = { name = "Espoirs et tragédies",
    done = function() return qdone(1634) end,
    run = function()
        if not qactive(1634) then if not npcStep("s1634", 153356296, 1515, "FECALINE") then return false end return end
        if not odone(1634, 9772) then
            -- Client actuel : ouvrage274, element489508/cell426 ; lecture validee
            -- depuis440 le26/10. Le cachev5 peut encore omettre sa position.
            if not go(153356296) or not goToCellId(440) then return end
            if map:useById(489508,184) then
                delay(500)
                if document:isOpen() then document:close() end
                delay(500)
            end
            return
        end
        if not odone(1634, 9773) then if not npcStep("f1634", 153356296, 1515, "FECALINE") then return false end end
        if not odone(1634, 9774) then solveMap(154010883, function() return odone(1634, 9774) end, "VIEUX", { noUse = true, prefs = { 24918, 24917, 24916, 24915, 24914, 24911, 24910, 24909, 24908 } }) end
        -- GP1: les trois chercheurs peuvent etre traites independamment. Limiter les
        -- essais par objectif pour ne pas boucler sur un dialogue sans reponse.
        espoirsAttempts = espoirsAttempts or {}
        if not odone(1634, 9775) and (espoirsAttempts[9775] or 0) < 2 then
            espoirsAttempts[9775] = (espoirsAttempts[9775] or 0) + 1
            -- 25128 ouvre sa biographie sans suite. Le jeu propose aussi
            -- 25137 : proposer les des, puis les jeter. Objectif9775 confirme.
            if go(153357316) then
                talk(2909,{25137,25130,25132,25134,25136,25139,25141,25143,25145,25127,25126,25125},"ALEHA")
            end
            quest:refresh(1634)
        end
        if not odone(1634, 9776) and (espoirsAttempts[9776] or 0) < 2 then
            espoirsAttempts[9776] = (espoirsAttempts[9776] or 0) + 1
            solveMap(153879809, function() return odone(1634, 9776) end, "VOLEUR", { noUse = true, prefs = { 25150, 25151, 25149, 25148, 25147 } })
            quest:refresh(1634)
        end
        if not odone(1634,9774) or not odone(1634,9775) or not odone(1634,9776) then
            printError("Espoirs et tragedies : chercheur non confirme apres essais ; verifier la quete.")
            return false
        end
        if not npcStep("e1634", 153356296, 1515, "FECALINE") then return false end
    end }

-- 4. Série métiers de Berb Nhin (1649 -> 2512 -> 1650 -> 1651 -> 2513)
local BERB_MAP, BERB, KNIFE, VIANDE = 153878787, 2899, 30832, 16663
local function berb(k) return npcStep(k, BERB_MAP, BERB, "BERB") end
local function viandeNeeded()
    local n = 0
    if not qdone(2512) and not has(16670) then n = n + 1 end
    if not qdone(2513) and not has(30701) then n = n + 2 end
    if not qdone(2513) and not has(30828) then n = n + 2 end
    return n
end
local function huntMeat()
    if has(KNIFE) and inventory:itemPosition(KNIFE) == 63 then inventory:equip(KNIFE, 1) end
    return farm("route")
end
P[#P+1] = { name = "Série métiers (Berb Nhin)",
    done = function() return qdone(2513) end,
    run = function()
        if not qdone(1649) then
            if not qactive(1649) then if not berb("s1649") then return false end return end
            if not has(1813) and not has(1782, 8) then return gatherZ("lac", { 1782 }) end
            if not has(289, 12) or not has(421, 12) then return gatherZ("champs", { 289, 421 }) end
            if not has(468) then craftAt(153354242, 27, { {289, 4} }, "PAIN") end
            if not has(1813) then craftAt(153354246, 135, { {1782, 4} }, "GOUJON") end
            if not has(1182) then craftAt(153355270, 23, { {421, 4} }, "POTION") end
            if not berb("e1649") then return false end return
        end
        if not qdone(2512) then
            if not qactive(2512) then if not berb("s2512") then return false end return end
            if cnt(VIANDE) < viandeNeeded() then return huntMeat() end
            if not has(16670) then craftAt(153355268, 134, { {VIANDE, 1} }, "BOUILLON") end
            if not berb("e2512") then return false end return
        end
        if not qdone(1650) then
            if not qactive(1650) then if not berb("s1650") then return false end return end
            if cnt(303) + 10 * cnt(16489) < 20 then return gatherZ("foret", { 303 }) end
            if cnt(312) + 10 * cnt(16440) < 20 then return gatherZ("mine", { 312 }) end
            while cnt(16489) < 2 and has(303, 10) do if not craftAt(153355266, 101, { {303, 10} }, "PLANCHE") then break end end
            while cnt(16440) < 2 and has(312, 10) do if not craftAt(153355264, 32, { {312, 10} }, "FERRITE") then break end end
            if not berb("e1650") then return false end return
        end
        if not qdone(1651) then
            if not qactive(1651) then if not berb("s1651") then return false end return end
            local need = { {16512,2,"champs"}, {16518,2,"route"}, {16513,2,"champs"}, {16522,2,"lac"}, {1984,2,"route"}, {16511,2,"paturages"} }
            for _, n in ipairs(need) do if not has(n[1], n[2]) then printWarning("Boune : il manque " .. getItemName(n[1])) return farm(n[3]) end end
            if not has(1782, 4) then return gatherZ("lac", { 1782 }) end
            if not has(8538) then craftAt(153355272, 12, { {16512, 2}, {1782, 2} }, "S'MESME") end
            if not has(8537) then craftAt(153355272, 12, { {16518, 2}, {289, 2} }, "PLUSSAIN") end
            if not has(8535) then craftAt(153354244, 13, { {16513, 2}, {1782, 2} }, "INCRUSTES") end
            if not has(8536) then craftAt(153354244, 13, { {16522, 2}, {421, 2} }, "SPAMETTE") end
            if not has(8534) then craftAt(153354244, 63, { {1984, 2}, {289, 2} }, "CAPE") end
            if not has(8533) then craftAt(153354244, 63, { {16511, 2}, {421, 2} }, "FLOUDE") end
            if not berb("e1651") then return false end return
        end
        if not qactive(2513) then if not berb("s2513") then return false end return end
        if cnt(VIANDE) < viandeNeeded() then return huntMeat() end
        if not has(16524, 3) or not has(1984, 5) then return farm("cimetiere") end
        if not has(16489) and has(303, 10) then craftAt(153355266, 101, { {303, 10} }, "PLANCHE") end
        if not has(16440) and has(312, 10) then craftAt(153355264, 32, { {312, 10} }, "FERRITE") end
        if not has(30701) then craftAt(153354240, 156, { {16489, 1}, {VIANDE, 2} }, "HALTE EFKAT") end
        if not has(30828) then craftAt(153355264, 20, { {16440, 1}, {VIANDE, 2} }, "BOUNIHIMEE") end
        if not has(8545) then craftAt(153354248, 171, { {16524, 3}, {1984, 5} }, "CLEF KARDORIM") end
        if not berb("e2513") then return false end
    end }

-- 5. Série exploration de Ternette Nhin (1639 -> 1640 -> 1641)
local TER_MAP, TER = 154010371, 2905
local STELES = {
    { obj = 9817, maps = { 153879811, 153880323, 153880835, 153878787 } },
    { obj = 9818, maps = { 154010372, 153878788, 154010886, 154011397, 154011398, 153878789 } },
    { obj = 9819, maps = { 154010370, 153878786, 154010882, 154010881, 154010369, 153878785, 154010112, 154010624, 153878528, 153878529, 154010113 } },
    { obj = 9820, maps = { 153879300, 153879812, 153879813, 153879301, 153880324, 153880325, 153880836 } },
    { obj = 9821, maps = { 153879298, 153879810, 153880322, 153879297, 153879809, 153880321, 153879040, 153879552 } },
}
local function useNamed(mapId, patterns, check, label)
    if check() then return true end
    if not travelTo(mapId) then return false end
    local list = {}
    for _, it in ipairs(map:getInteractives()) do
        local n = string.lower(tostring(it.name))
        for _, p in ipairs(patterns) do if string.find(n, p, 1, true) then list[#list+1] = it break end end
    end
    for _, it in ipairs(list) do
        if check() then return true end
        if getMapId() ~= mapId and not travelTo(mapId) then return check() end
        map:useById(it.elementId, -1)
        delay(1500) leaveDialog() delay(500)
    end
    return check()
end
P[#P+1] = { name = "Série exploration (Ternette Nhin)",
    done = function() return qdone(1641) end,
    run = function()
        if not qdone(1639) then
            if not qactive(1639) then if not npcStep("s1639", TER_MAP, TER, "TERNETTE") then return false end return end
            if not odone(1639, 9814) then
                if not npcStep("x1639",153879813,2882,"XELORA",{25020,25019,25018,25017,25016}) then return false end
                if not odone(1639,9814) then return end
            end
            if not odone(1639, 9815) then useNamed(153879813, { "zaap" }, function() return odone(1639, 9815) end) end
            if not npcStep("e1639", TER_MAP, TER, "TERNETTE") then return false end return
        end
        if not qdone(1640) then
            if not qactive(1640) then if not npcStep("s1640", TER_MAP, TER, "TERNETTE") then return false end return end
            for _, z in ipairs(STELES) do
                if not odone(1640, z.obj) then
                    for _, m in ipairs(z.maps) do
                        if useNamed(m, { "utiliser", "interactif (type", "stèle", "vestige" }, function() return odone(1640, z.obj) end) then break end
                    end
                end
            end
            if not npcStep("e1640", TER_MAP, TER, "TERNETTE") then return false end return
        end
        if not qactive(1641) then if not npcStep("s1641", TER_MAP, TER, "TERNETTE") then return false end return end
        if not odone(1641, 9823) then npcStep("m1641", 154010374, 2887, "MATU") end
        if not odone(1641, 9824) then useNamed(154010374, { "regarder", "longue" }, function() return odone(1641, 9824) end) end
        if not odone(1641, 9825) then npcStep("g1641", 154010113, 2906, "GALILEA") end
        if not odone(1641, 9826) then useNamed(154010113, { "regarder", "longue" }, function() return odone(1641, 9826) end) end
        if not npcStep("e1641", TER_MAP, TER, "TERNETTE") then return false end
    end }

-- 6. Série milice (1642 -> 1648)
local CAP_MAP, CAP = 153356294, 2880
local function cap(k) return npcStep(k, CAP_MAP, CAP, "CAPITAINE") end
P[#P+1] = { name = "Série milice (Capitaine des Kerubims)",
    done = function() return qdone(1648) end,
    run = function()
        if not qdone(1642) then
            if not qactive(1642) then if not cap("s1642") then return false end return end
            if not odone(1642, 9829) then
                if not npcStep("myn1", 153356292, 2894, "MYNERVE") then return false end
                delay(2000) waitFight() return
            end
            if not odone(1642, 9830) then if not npcStep("myn2", 153356292, 2894, "MYNERVE") then return false end end
            if not cap("e1642") then return false end return
        end
        local chain = {
            { 1643, { {9832,970},{9833,979},{9834,980},{9835,981} }, "champs" },
            { 1644, { {9837,4109},{9838,4108},{9839,4110} }, "lac" },
            { 1645, { {9841,972},{9842,973},{9843,971},{9844,984} }, "paturages" },
            { 1646, { {9846,4105},{9847,4106},{9848,982} }, "foret" },
        }
        for _, c in ipairs(chain) do
            if not qdone(c[1]) then
                if not qactive(c[1]) then if not cap("s" .. c[1]) then return false end return end
                local r = hunt(c[1], c[2], c[3])
                if r then return r end
                if not cap("e" .. c[1]) then return false end return
            end
        end
        if not qdone(1647) then
            if not qactive(1647) then if not cap("s1647") then return false end return end
            if not odone(1647, 9850) then
                if not try("kruella", 30) then return false end
                travelTo(153879040)
                for _, p in ipairs(map:getNpcs()) do talk(p.id, nil, "KRUELLA?") end
                delay(1500) waitFight()
                if odone(1647, 9850) then return end
                FORCE_MONSTERS = { 4097 }
                fight() waitFight()
                return
            end
            if not cap("e1647") then return false end return
        end
        if not qactive(1648) then if not cap("s1648") then return false end return end
        local r = hunt(1648, { {9852,4046},{9853,4047},{9854,4048},{9855,4049} }, "cimetiere")
        if r then return r end
        if not odone(1648, 9857) then
            if not try("percy", 6) then return false end
            if not odone(1648, 9859) then solveMap(153356288, function() return odone(1648, 9859) end, "TOMBE", { noNpc = true }) end
            travelTo(153356288)
            talk(2938, { 25086 }, "PERCY")
            delay(2000) waitFight()
            return
        end
        if not cap("e1648") then return false end
    end }

-- 7. Un peu de pigment (Marylork) — Poudre d'Aminite (Mine)
P[#P+1] = { name = "Un peu de pigment",
    done = function() return qdone(1655) end,
    run = function()
        if not qactive(1655) then if not npcStep("s1655", 153880325, 2888, "MARYLORK") then return false end return end
        if not has(16999, 3) then return farm("mine", { 4107 }) end
        if not npcStep("e1655", 153880325, 2888, "MARYLORK") then return false end
    end }

-- 8. Mort au rat ! (Grobid, cave de la taverne)
P[#P+1] = { name = "Mort au rat !",
    done = function() return qdone(1633) end,
    run = function()
        -- 1) l'offre d'emploi (affiche 489412, façade de la taverne) ouvre un document qui propose la quête
        if not qactive(1633) then
            if not try("s1633", 3) then return false end
            if go(153878787) then
                local opened = map:useById(489412,184)
                local doc = opened and document:get() or nil
                local ok = false
                if doc then
                    ok = quest:start(1633)
                    pcall(function() document:close() end)
                end
                print("Mort au rat ! : quête démarrée = " .. tostring(qactive(1633)))
                if not qactive(1633) then          -- secours : acceptation à la main
                    printWarning("Mort au rat ! : clique « Accepter » sur l'offre d'emploi en jeu (3 min max)")
                    discord("⚠️ Accepte « Mort au rat ! » sur l'affiche de la taverne (3 min)")
                    map:useById(489412, 184)
                    for _ = 1, 90 do if qactive(1633) then break end delay(2000) end
                    leaveDialog()
                end
            end
            return
        end
        if not odone(1633, 9770) then
            if not try("rat", 6) then return false end
            -- 2) une limonade (1 kama) chez Grobid, 3) la tireuse de la cave fait sortir le rat
            if not has(8543) and not odone(1633, 9769) then
                travelTo(153357316)
                talk(2885, { 25036, 25034 }, "GROBID")
            end
            -- 3) la limonade sur la tireuse (élément 489505) fait sortir le rat, qui est un PNJ : « Attaquer le rat »
            if not odone(1633, 9769) then
                solveMap(153358340, function() return odone(1633, 9769) or isInFight() end, "CAVE", { noNpc = true })
                return
            end
            if go(153358340) then
                talk(2912, { 25037 }, "RAT")
                delay(1500) waitFight()
            end
            return
        end
        if not npcStep("e1633", 153357316, 2885, "GROBID", { 25033, 25027, 25032, 25026 }) then return false end
    end }

-- 9. La galette secrète (Anta Brok)
P[#P+1] = { name = "La galette secrète",
    done = function() return qdone(1637) end,
    run = function()
        if not qactive(1637) then if not npcStep("s1637", 153879298, 2886, "ANTA", { 25216, 25214, 25213 }) then return false end return end
        if not odone(1637, 9805) and has(16517) then useItem(16517, 1) delay(1500) leaveDialog() end
        if not odone(1637, 9806) and not has(16520) then
            if not has(28256, 2) then return farm("champs", { 970 }) end
            if not has(519, 4) then return farm("champs") end
            if not has(385, 4) then return farm("paturages") end
            if not has(1984, 4) then return farm("route") end
            if not has(289, 10) or not has(421, 5) then return gatherZ("champs", { 289, 421 }) end
            if not has(6765) then
                travelTo(153357316)
                talk(2885, { 25036, 25035 }, "GROBID")
            end
            craftAt(153354242, 27, { {289,10},{519,4},{28256,2},{6765,1},{385,4},{1984,4} }, "GALETTE")
            return
        end
        if not odone(1637, 9807) then if not npcStep("a1637", 153879298, 2886, "ANTA", { 25218, 25217 }) then return false end return end
        if not has(16509) then
            if not has(421, 5) then return gatherZ("champs", { 421 }) end
            if not npcStep("p1637", 153879811, 2881, "PIPELETTE", { 25229, 25228, 25227 }) then return false end
            return
        end
        if not npcStep("e1637", 153879298, 2886, "ANTA", { 25224, 25223 }) then return false end
    end }

local function cryptRooms()
    local groups = map:getMonsters()
    if #groups > 0 then FORCE_MONSTERS = {} MAX_MONSTERS = 8 fight() waitFight() return end
    local here = getMapId()
    for _, d in ipairs({ "right", "top", "left", "bottom" }) do
        if map:neighbourId(d) ~= 0 and changeMap(d) and getMapId() ~= here then return end
    end
    solveMap(nil, function() return getMapId() ~= here or map:getMonsters()[1] ~= nil end, "PORTE", { noNpc = true })
end

-- 10. Cryptologie (avant le Milimilou : la crypte mène aussi à son antre) (donjon de Kardorim)
P[#P+1] = { name = "Cryptologie (Crypte de Kardorim)",
    prio = function() return map:currentSubAreaId() == 447 end,   -- déjà dans la crypte : on la finit d'abord
    done = function() return qdone(1638) end,
    run = function()
        if not has(8545) and not qactive(1638) then
            if not has(16524, 3) or not has(1984, 5) then return farm("cimetiere") end
            craftAt(153354248, 171, { {16524, 3}, {1984, 5} }, "CLEF KARDORIM")
            return
        end
        local inCrypt = map:currentSubAreaId() == 447
        if not inCrypt and not odone(1638, 9812) then
            if not try("crypte", 6) then return false end
            npcStep("kc1638", 153881600, 2907, "KLASMOR", { 24967, 24971, 24970, 24968 })
            tries.kc1638 = 0
            return
        end
        if not odone(1638, 9812) then
            if not try("salle", 60) then return false end
            cryptRooms()
            return
        end
        if not npcStep("kd1638", 152835072, 2936, "KARDORIM") then return false end
    end }

-- 11. Dans la gueule du Milimilou (Fécaline -> Klasmor -> grille rouillée -> antre -> Hargnok)
local ANTRE, GRILLE_MAP, GRILLE_CELL, MILI, HARGNOK = 152836096, 153881088, 284, 2911, 2898
local function fullLife(pct)
    for _ = 1, 90 do
        local ok, p = pcall(function() return character:lifePointsP() end)
        if not ok or not p or p < 0 or p >= (pct or 95) then return end
        if _ == 1 then print("Regen avant le Milimilou (" .. p .. " %)") end
        delay(2000)
    end
end
P[#P+1] = { name = "Dans la gueule du Milimilou",
    -- déjà dans l'antre ET la quête y mène (Klasmor vu) : on finit ici ; sinon on en ressort (phase « Sortie de la crypte »)
    prio = function() return getMapId() == ANTRE and not qdone(1635) and odone(1635, 9783) end,
    done = function() return qdone(1635) end,
    run = function()
        local inAntre = getMapId() == ANTRE
        if not qactive(1635) then if not npcStep("s1635", 153356296, 1515, "FECALINE") then return false end return end
        if not odone(1635, 9783) and not inAntre then
            if not npcStep("k1635", 153881600, 2907, "KLASMOR", { 24989, 24987, 24988, 24985, 24982 }) then return false end
            return
        end
        if not odone(1635, 9785) then
            if not try("milimilou", 6) then return false end
            if not inAntre then
                -- raccourci : la vieille grille rouillée à l'ouest de Klasmor (cellule 284)
                if go(GRILLE_MAP) then
                    fullLife(95)
                    pcall(function() map:door(GRILLE_CELL) end)
                    delay(1500)
                    if getMapId() ~= ANTRE then pcall(function() map:moveToCell(GRILLE_CELL) end) delay(2000) end
                end
                if getMapId() ~= ANTRE then printWarning("Antre du Milimilou : entrée non trouvée") end
                return
            end
            fullLife(95)
            -- pot de confiture -> « Défendre chèrement sa vie » (jamais « S'enfuir »)
            talk(MILI, { 25006, 25009, 25005, 25007, 25002, 25004 }, "MILIMILOU")
            delay(1500) waitFight()
            return
        end
        if not odone(1635, 9786) then
            if inAntre then talk(HARGNOK, nil, "HARGNOK") return end
            if not npcStep("h1635", ANTRE, HARGNOK, "HARGNOK") then return false end
            return
        end
        if inAntre then talk(MILI, { 25003, 25010, 25008 }, "SORTIE ANTRE") delay(1500) return end
        if not npcStep("e1635", 153356296, 1515, "FECALINE") then return false end
    end }

-- 11b. Succès du donjon de Kardorim (challenges de succès : Premier, Zombie, Duo)
-- Les succès sont liés au compte : on lit ceux déjà obtenus à la connexion, puis ceux validés pendant le script.
DONJON_CH = { [117] = "Kardorim (Premier)", [115] = "Kardorim (Zombie)", [102] = "Kardorim (Duo)" }
local DONJON_ACH = { [117] = 574, [115] = 571, [102] = 487 }
local function achDone(achId)
    local ok, d = pcall(function() return achievement:isDone(achId) end)
    return ok and d == true
end
local function donjonMissing()
    local miss = {}
    for ch, ach in pairs(DONJON_ACH) do
        if not achDone(ach) and global:remember("donjon_ch_" .. ch) ~= 1 then miss[#miss+1] = DONJON_CH[ch] end
    end
    return miss
end
P[#P+1] = { name = "Succès du donjon de Kardorim",
    prio = function() return qdone(1638) and map:currentSubAreaId() == 447 and getMapId() ~= ANTRE and #donjonMissing() > 0 end,
    -- une seule run : on tente le maximum de succès pendant la run de la quête, jamais de nouvelle entrée pour le manquant
    done = function() return not qdone(1638) or #donjonMissing() == 0 or map:currentSubAreaId() ~= 447 end,
    run = function()
        local inCrypt = map:currentSubAreaId() == 447
        if not inCrypt then
            if not try("donjon_entree", 6) then return false end
            printSuccess("Donjon de Kardorim : succès manquants -> " .. table.concat(donjonMissing(), ", "))
            if not has(8545) then
                if not has(16524, 3) or not has(1984, 5) then return farm("cimetiere") end
                craftAt(153354248, 171, { {16524, 3}, {1984, 5} }, "CLEF KARDORIM")
                return
            end
            global:addInMemory("donjon_runs", (global:remember("donjon_runs") or 0) + 1)
            npcStep("kd_in", 153881600, 2907, "KLASMOR", { 24970, 24968 })
            tries.kd_in = 0
            return
        end
        if getMapId() == 152835072 then                 -- salle de Kardorim (après le boss) : on ressort
            talk(2936, nil, "KARDORIM")
            if getMapId() == 152835072 then solveMap(nil, function() return getMapId() ~= 152835072 end, "SORTIE", { noNpc = true }) end
            return
        end
        if not try("donjon_salle", 80) then return false end
        cryptRooms()
    end }

-- 11c. Sortie de la crypte une fois tout fini (la salle de Kardorim donne sur l'antre du Milimilou, à gauche)
P[#P+1] = { name = "Sortie de la crypte",
    -- dans l'antre, on ne reste que si la quête du Milimilou y est en cours (Klasmor vu)
    prio = function() return map:currentSubAreaId() == 447 and qdone(1638) and (getMapId() ~= ANTRE or qdone(1635) or not odone(1635, 9783)) end,
    done = function() return map:currentSubAreaId() ~= 447 or not qdone(1638) or (getMapId() == ANTRE and not qdone(1635) and odone(1635, 9783)) end,
    run = function()
        if not try("sortie_crypte", 10) then return false end
        local here = getMapId()
        local left, why = map:leaveDungeon()
        if left and getMapId() ~= here then return end
        if why == "fight" then return end
        if here == ANTRE then
            -- Milimilou présent : sa sortie ; sinon Hargnok : « Quitter la crypte du Milimilou » (25014)
            local mili = false
            for _, p in ipairs(map:getNpcs()) do if p.id == MILI or p.npcId == MILI then mili = true end end
            if mili then talk(MILI, { 25003, 25010, 25008 }, "SORTIE ANTRE")
            else
                for _, p in ipairs(map:getNpcs()) do talk(p.id, { 25014 }, "SORTIE ANTRE " .. tostring(p.name)) if getMapId() ~= here then break end end
            end
            delay(1500)
            if getMapId() ~= here then return end
        end
        if here == 152835072 then
            if changeMap("left") and getMapId() ~= here then return end
            talk(2936, nil, "KARDORIM")
            if getMapId() ~= here then return end
        end
        if getMapId() == here then printWarning("Sortie de donjon refusée : " .. tostring(why)) end
    end }

-- 12. Succès de découverte des zones
local DECOUVERTE = { 154010370, 154010372, 153880064, 153879297, 153357312, 153879300, 153878787, 153357316 }
P[#P+1] = { name = "Découverte des zones",
    done = function() return global:remember("incarnam_zones") == 1 end,
    run = function()
        for _, m in ipairs(DECOUVERTE) do travelTo(m) end
        global:addInMemory("incarnam_zones", 1)
    end }

-- 13. Panoplie de l'Aventurier complète et équipée
P[#P+1] = { name = "Panoplie de l'Aventurier",
    done = panoComplete,
    run = function()
        equipPano()
        if panoComplete() then return end
        for _, p in ipairs(PANO) do
            if not has(p.gid) then
                if not tries["pano" .. p.gid] then printWarning("Panoplie : il manque " .. getItemName(p.gid) .. " -> farm " .. p.zone) tries["pano" .. p.gid] = 1 end
                return farm(p.zone)
            end
        end
        printWarning("Panoplie : niveau insuffisant pour certaines pièces -> farm route des âmes")
        return farm("champs")
    end }

-- 14. Vente du butin à l'HDV des ressources d'Incarnam
local function sellLoot(hdvMap)
    if not goToHdv(hdvMap) then return 0 end
    local n = 0
    for _, it in ipairs(inventory:items()) do
        if not it.equipped and not GARDER[it.gid] and sale:AvailableSpace() ~= 0 then
            local qty = it.qty or 0
            local lot = qty >= 100 and 100 or (qty >= 10 and 10 or 1)
            local price = sale:GetPriceItem(it.gid, lot)
            if price and price > 1 then
                local nb = math.min(math.floor(qty / lot), 5)
                for _ = 1, nb do
                    if sale:SellItem(it.gid, lot, math.max(1, price - 1)) then n = n + 1 else break end
                end
            end
        end
    end
    leaveDialog()
    printSuccess(n .. " lot(s) mis en vente")
    return n
end
P[#P+1] = { name = "Vente du butin (Incarnam)",
    done = function() return true end, -- vente non necessaire aux quetes
    run = function()
        sellLoot(153879299)
        global:addInMemory("incarnam_vente", 1)
    end }

-- 15. Destination Astrub : Anemo, puis le PNJ « Portail vers Astrub » (le havre-sac/zaap ne mène pas à Astrub depuis Incarnam)
local INCARNAM_AREA, PORTAIL_MAP, PORTAIL_NPC = 45, 153880835, 4398
P[#P+1] = { name = "Destination Astrub",
    done = function() return qdone(2004) end,
    run = function()
        if not qactive(2004) then if not npcStep("s2004", 153356296, 1515, "FECALINE") then return false end return end
        if not odone(2004, 13767) then if not npcStep("an2004", PORTAIL_MAP, 4360, "ANEMO", { 36614 }) then return false end end
        if map:currentAreaId() == INCARNAM_AREA then
            if not try("portail", 6) then return false end
            if go(PORTAIL_MAP) then
                talk(PORTAIL_NPC, { 36980, 36977 }, "PORTAIL")
                delay(3000)
            end
            return
        end
        if not npcStep("en2004", 192416776, 4355, "ENVOYEE") then return false end
    end }

-- 16. Kamas : au moins KAMAS_CIBLE en inventaire (banque d'Astrub + farm/vente si besoin)
local farmCycles = 0
P[#P+1] = { name = "Kamas (" .. KAMAS_CIBLE .. ")",
    done = function() return getKamas() >= KAMAS_CIBLE end,
    run = function()
        travelTo(192415750)
        local bk = -1
        if bank:open() then
            bk = bank:getKamas()
            local manque = KAMAS_CIBLE - getKamas()
            if bk > 0 and manque > 0 then bank:withdrawKamas(math.min(bk, manque)) end
            bank:close()
        end
        print("Kamas inventaire = " .. getKamas() .. " | banque = " .. tostring(bk))
        if getKamas() >= KAMAS_CIBLE then return end
        -- pas assez : on farme les Champs d'Astrub puis on vend à l'HDV des ressources d'Astrub
        farmCycles = farmCycles + 1
        if farmCycles % 3 == 0 then sellLoot(191104004) return end
        return farm("astrub")
    end }

-- 17. Zaap d'Astrub puis déconnexion
P[#P+1] = { name = "Zaap d'Astrub et déconnexion", final = true,
    done = function() return false end,
    run = function()
        -- un compte neuf ne connaît pas encore ce zaap : on y va à pied depuis le Temple des Douze, puis on l'active
        if not try("zaap_final", 5) then printRecap() delay(3000) stopScript() return false end
        if not go(191105026) then delay(2000) return end
        for _, it in ipairs(map:getInteractives()) do
            if string.find(string.lower(tostring(it.name)), "zaap", 1, true) then map:useById(it.elementId, -1) delay(2000) leaveDialog() break end
        end
        printRecap()
        notify(MESSAGE_FIN)
        delay(4000)                      -- laisse partir le message Discord avant la déconnexion
        stopScript()
        return false
    end }


-- =========================== STATISTIQUES ==========================
-- Gardées en mémoire du bot : un redémarrage du script ne remet pas le compteur à zéro.
function xpNow() local ok, v = pcall(function() return character:experience() end) return (ok and tonumber(v)) or -1 end
function saveStats() pcall(function() global:addInMemory("incarnam_stats", STATS) end) end
local function loadStats()
    local ok, s = pcall(function() return global:remember("incarnam_stats") end)
    if ok and type(s) == "table" and s.start then STATS = s return end
    STATS = { start = os.time(), fights = 0, wins = 0, deaths = 0, chOk = 0, chKo = 0,
              kamas0 = getKamas(), xp0 = xpNow(), lvl0 = character:level() }
    saveStats()
end
local function fmtTime(s)
    return string.format("%dh %02dmin %02ds", math.floor(s / 3600), math.floor(s % 3600 / 60), s % 60)
end
local QUETES_INCARNAM = {
    {1631,"Réponses à tout"},{1632,"Le village dans les nuages"},{1634,"Espoirs et tragédies"},
    {1649,"Berb : 1"},{2512,"Berb : 2"},{1650,"Berb : 3"},{1651,"Berb : 4"},{2513,"Berb : 5"},
    {1639,"Transport peu commun"},{1640,"Des vestiges de légende"},{1641,"Vu du ciel"},
    {1642,"Mise à l'épreuve"},{1643,"Champs de bataille"},{1644,"Coups d'épée dans l'eau"},{1645,"Décime-moi des bouftous"},
    {1646,"Chasse aux chapardams"},{1647,"Leçon d'humilité"},{1648,"Des chafers qui marchent"},
    {1655,"Un peu de pigment"},{1633,"Mort au rat !"},{1637,"La galette secrète"},{1638,"Cryptologie"},
    {1635,"Dans la gueule du Milimilou"},{2004,"Destination Astrub"},
}
function printRecap()
    local s = STATS or {}
    local xp = xpNow()
    local ok, manquantes = 0, {}
    for _, q in ipairs(QUETES_INCARNAM) do
        if qdone(q[1]) then ok = ok + 1 else manquantes[#manquantes+1] = q[2] end
    end
    local pano = {}
    for _, p in ipairs(PANO) do
        local pos = inventory:itemPosition(p.gid)
        if pos == -1 or pos == 63 then pano[#pano+1] = getItemName(p.gid) end
    end
    local L = {
        "================ RÉCAPITULATIF INCARNAM ================",
        "Personnage      : " .. character:name() .. " (niveau " .. tostring(s.lvl0) .. " -> " .. character:level() .. ")",
        "Durée totale    : " .. fmtTime(os.time() - (s.start or os.time())),
        "Combats         : " .. tostring(s.fights) .. " (" .. tostring(s.wins) .. " gagnés)",
        "Morts           : " .. tostring(s.deaths),
        "Challenges      : " .. tostring(s.chOk or 0) .. " réussis, " .. tostring(s.chKo or 0) .. " ratés",
        "XP gagnée       : " .. ((xp >= 0 and (s.xp0 or -1) >= 0) and tostring(xp - s.xp0) or "inconnue"),
        "Kamas générés   : " .. tostring(getKamas() - (s.kamas0 or 0)) .. " (inventaire : " .. getKamas() .. ")",
        "Quêtes          : " .. ok .. "/" .. #QUETES_INCARNAM .. (#manquantes > 0 and (" (manquantes : " .. table.concat(manquantes, ", ") .. ")") or " (toutes terminées)"),
        "Panoplie        : " .. (#pano == 0 and "complète et équipée" or ("manque " .. table.concat(pano, ", "))),
        "Phases sautées  : " .. ((SKIPPED_LIST and #SKIPPED_LIST > 0) and table.concat(SKIPPED_LIST, ", ") or "aucune"),
        "Carte finale    : " .. tostring(getMapId()) .. " (zaap d'Astrub)",
        "========================================================",
    }
    for _, l in ipairs(L) do printSuccess(l) end
    printSuccess(MESSAGE_FIN)
    FINISHED = true
    discord("✅ **" .. MESSAGE_FIN .. "**\n```\n" .. table.concat(L, "\n") .. "\n```")
    pcall(function() global:deleteMemory("incarnam_stats") end)
end

-- =========================== BOUCLE ================================
SKIPPED, SKIPPED_LIST = {}, {}
local INIT = false
function move()
    if getCharacterName()~="Valrianess" or getServerName()~="Kourial" then
        printError("Identite inattendue") stopScript() return false
    end
    if not quest:loaded() then printWarning("Etat des quetes inconnu") delay(3000) return true end
    if isDead() then printError("Personnage mort : diagnostic requis") stopScript() return false end
    if isInFight() then delay(1500) return end
    -- juste après un combat, la carte de combat est encore active : on laisse le jeu remettre la vraie carte
    -- (move() est rappelé avant onFightEnd : on attend aussi après chaque parcours de farm)
    if FIGHT_JUST_ENDED or AFTER_ROUTE then FIGHT_JUST_ENDED, AFTER_ROUTE = false, false delay(2500) end
    if not INIT then
        -- attend que le personnage soit chargé (nom, niveau, sous-zone) avant de décider quoi faire
        WAIT_INIT = (WAIT_INIT or 0) + 1
        if WAIT_INIT < 15 and ((character:level() or 0) < 1 or (map:currentSubAreaId() or 0) <= 0) then print("Attente du chargement du personnage…") delay(2000) return end
        INIT = true
        combat:setAutoFight(true)
        combat:setAutoSpells(true)
        combat:setSpeed("Turbo")
        combat:setStyle("Fleeing")
        combat:setKiteRange(2,4)
        pcall(function() combat:setAutoPreFight(true) end)
        combat:setChallengeMode("off")
        loadStats()
        printSuccess("Incarnam complet : démarrage (" .. character:name() .. ", niveau " .. character:level() .. ")")
        discord("▶️ Incarnam complet : démarrage — " .. character:name() .. ", niveau " .. character:level() .. ", " .. getKamas() .. " kamas")
    end
    -- dialogue PNJ ouvert sans qu'on l'ait demandé (ex. Andrée Inkarnay à la sortie du Temple) : il bloque
    -- tous les déplacements (« porte non aboutie (Refused) ») -> « L'ignorer et sortir du Temple » (83190) ou on ferme
    local okq, q = pcall(function() return npc:question() end)
    if okq and q then
        local pick
        for i, id in ipairs(q.replies or {}) do
            local t = string.lower(tostring((q.replyTexts or {})[i]))
            if id == 83190 or string.find(t, "sortir du temple", 1, true) then pick = id end
        end
        print("Dialogue ouvert (" .. string.sub(tostring(q.text), 1, 50) .. "…) -> " .. tostring(pick or "fermé"))
        if pick then pcall(function() npc:reply(pick) end) delay(1000) end
        pcall(function() leaveDialog() end)
        delay(1000)
        return
    end
    -- coincé dans le havre-sac (trajet raté) : on en ressort
    if map:currentSubAreaId() == 851 then enterHavenBag() delay(3000) return end
    housekeeping()
    -- phases prioritaires (ex. : on est dans le donjon, on le termine avant tout)
    local order = {}
    for _, ph in ipairs(P) do if ph.prio and ph.prio() then order[#order+1] = ph end end
    for _, ph in ipairs(P) do order[#order+1] = ph end
    for _, ph in ipairs(order) do
        local ok, d = pcall(ph.done)
        if not (ok and d) and not SKIPPED[ph.name] then
            if CURRENT ~= ph.name then CURRENT = ph.name printSuccess("=== PHASE : " .. ph.name .. " ===") end
            local r = (ph.run())
            if r ~= nil and type(r) == "table" then AFTER_ROUTE = true return r end
            if r == false then
                if ph.final then return false end
                -- une phase bloquée ne bloque pas tout le script : on la saute et on continue
                -- La phase reste non terminee et sera reprise apres correction.
                printError("Phase bloquee : " .. ph.name .. " : arret, aucune validation fictive")
                stopScript() return false

            end
            return
        end
    end
    return false
end

function onFightEnd(r)
    if not r or r.won~=true then printError("Defaite ou issue inconnue : arret") stopScript() return end
    FIGHT_JUST_ENDED = true
    if r and r.won then
        for _, c in ipairs(r.challenges or {}) do
            local ok = c.success == true or c.status == "success" or c.status == "succeeded"
            if ok and DONJON_CH[c.id] then pcall(function() global:addInMemory("donjon_ch_" .. c.id, 1) end) printSuccess("Succès de donjon validé : " .. DONJON_CH[c.id]) end
        end
    end
    if not STATS then return end
    for _, c in ipairs((r and r.challenges) or {}) do
        if c.success or c.status == "success" then STATS.chOk = (STATS.chOk or 0) + 1 else STATS.chKo = (STATS.chKo or 0) + 1 end
    end
    STATS.fights = STATS.fights + 1
    if r and r.won then STATS.wins = STATS.wins + 1 end
    if r and r.lost then
        STATS.deaths = STATS.deaths + 1
        printWarning("Combat perdu : on se soigne avant de reprendre")
        discord("💀 Combat perdu (mort n°" .. STATS.deaths .. ") — phase : " .. tostring(CURRENT) .. " — niveau " .. character:level())
    end
    saveStats()
end

function stopped(reason)
    printWarning("Script arrêté (" .. tostring(reason) .. ") — phase : " .. tostring(CURRENT))
    if not FINISHED then discord("⏹️ Script arrêté (" .. tostring(reason) .. ") — phase : " .. tostring(CURRENT)) end
end
