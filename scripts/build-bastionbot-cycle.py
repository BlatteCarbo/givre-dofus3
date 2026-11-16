from pathlib import Path
root=Path(__file__).resolve().parent
sale=(root/'Bastionbot-Vente-Ressources.lua').read_text(encoding='utf-8-sig')
reprice=(root/'Bastionbot-Reprice-Groupe.lua').read_text(encoding='utf-8-sig')
sale=sale.replace('function move() sellBagResources() return false end','')
reprice=reprice.replace('function move() repriceListings() return false end','')
driver=(root/'Bastionbot-Farm-Cycle-Controller.lua').read_text(encoding='utf-8-sig')
(root/'Bastionbot-Multi-Metiers-Cycle.lua').write_text('-- Genere par build-bastionbot-cycle.py ; sources adjacentes.\n'+sale+'\n'+reprice+'\n'+driver,encoding='utf-8')
