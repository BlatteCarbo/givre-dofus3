import os, sys, re, json
sys.path.insert(0, os.environ['TEMP'])
from givre_control import call
name = 'Nuit - Minerbot peche'
code = call('read_saved_script', {'name':name})['code']
code = re.sub(r'local ROUTE = \{.*?\n\}', 'local ROUTE = {\n  {135171, "-8,3", 2},\n  {134659, "-7,3", 2},\n}', code, count=1, flags=re.S)
print(json.dumps(call('save_script', {'name':name,'code':code,'overwrite':True})))
print(json.dumps(call('run_saved_script', {'bot':'4bc58e7f9db345aaa2e7dfdaf9ae1726','name':name})))

# Test the bank loop now, rather than waiting for a full inventory tonight.
name = 'Nuit - Bastionbot orties'
code = call('read_saved_script', {'name':name})['code']
code = code.replace('local step = 0', 'local firstBank = true\nlocal step = 0')
code = code.replace('function move()\n', 'function move()\n  if firstBank then firstBank = false if not bank() then return false end end\n')
code = code.replace('  local deposited = exchange:putAllItems()', '  if bank:getKamas() >= 100000 then print("Reserve trajet : " .. tostring(bank:withdrawKamas(100000))) end\n  local deposited = exchange:putAllItems()')
print(json.dumps(call('validate_lua_script', {'code':code})))
print(json.dumps(call('save_script', {'name':name,'code':code,'overwrite':True})))
print(json.dumps(call('run_saved_script', {'bot':'c4a95aedecaf4a82803e6c64eb33ca38','name':name})))
