import os, sys, json
sys.path.insert(0, os.environ['TEMP'])
from givre_control import call
plans = [
 ('c4a95aedecaf4a82803e6c64eb33ca38', 'Nuit - Bastionbot orties'),
 ('4bc58e7f9db345aaa2e7dfdaf9ae1726', 'Nuit - Minerbot peche'),
 ('00fe352d06bf491fb5abcc2029ce84d7', 'Nuit - Foragerbot avoine')]
for bot, name in plans:
    code = call('read_saved_script', {'name':name})['code']
    code = code.replace('local firstBank = true\n', '')
    code = code.replace('  if firstBank then firstBank = false if not bank() then return false end end\n', '')
    code = '\n'.join(line for line in code.split('\n') if 'Reserve trajet' not in line)
    code = code.replace('local step = 0', 'local losses = 0\nlocal step = 0')
    code = code.replace('function move()\n', 'function move()\n  if losses >= 2 then printWarning("Deux defaites : arret pour diagnostic") return false end\n')
    code += '\nfunction onFightEnd(r)\n  if r.won == false then losses = losses + 1 end\n  print("Bilan protecteur : victoire=" .. tostring(r.won) .. " defaites=" .. losses)\nend\n'
    valid = call('validate_lua_script', {'code':code})
    if not valid.get('valid'): raise RuntimeError(valid)
    print(json.dumps(call('save_script', {'name':name,'code':code,'overwrite':True})))
    if not call('get_bot_state', {'bot':bot}).get('inFight'):
        print(json.dumps(call('run_saved_script', {'bot':bot,'name':name})))
