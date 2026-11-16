import sys, os, json
sys.path.insert(0, os.environ['TEMP'])
from givre_control import call

plans = [
    ('c4a95aedecaf4a82803e6c64eb33ca38', 'Nuit - Bastionbot orties', 192415750),
    ('00fe352d06bf491fb5abcc2029ce84d7', 'Nuit - Foragerbot avoine', 192415750),
    ('4bc58e7f9db345aaa2e7dfdaf9ae1726', 'Nuit - Minerbot peche', 84935175),
]
for bot, name, bank_map in plans:
    result = call('read_saved_script', {'name':name})
    code = result['code']
    # Keep the existing observed route, with a bank callback and bounded recovery.
    code = code.replace('local step = 0', 'local step = 0\nlocal failures = 0\nlocal empty = 0')
    code = code.replace('function move()\n', 'function move()\n  if getPodsPercent() >= MAX_PODS then return bank() end\n')
    code = code.replace('      return true\n', '      failures = failures + 1\n      if failures >= #ROUTE * 2 then printWarning("Circuit inaccessible : arret pour diagnostic") return false end\n      delay(2000)\n      return true\n')
    code = code.replace('  gatherAll()\n', '  failures = 0\n  local harvested = gatherAll()\n  if harvested then empty = 0 else empty = empty + 1 end\n  if empty >= #ROUTE then print("Circuit vide : pause de 30 secondes") delay(30000) empty = 0 end\n')
    code += f'''
function bank()
  print("Banque : depot et reprise du circuit")
  if not travelTo({bank_map}) then printWarning("Banque inaccessible") return false end
  if not npc:npcBank() then printWarning("Ouverture banque impossible") return false end
  local deposited = exchange:putAllItems()
  bank:close()
  if not deposited or getPodsPercent() >= MAX_PODS then printWarning("Depot non confirme : arret") return false end
  return true
end
'''
    validation = call('validate_lua_script', {'code':code})
    print(json.dumps({'name':name, 'validation':validation}))
    print(json.dumps(call('save_script', {'name':name, 'code':code, 'overwrite':True})))
