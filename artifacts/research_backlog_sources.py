import concurrent.futures,json,pathlib,urllib.request
OUT=pathlib.Path('docs/codex/agent-program-20261026')
d=json.loads((OUT/'github-evidence.json').read_text(encoding='utf8'))
def get(url,limit=200000):
 with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'Givre-research'}),timeout=25) as r:return r.read(limit).decode('utf8','replace')
t3=next(r for r in d['bridge_repositories'] if r['repo']=='pingdotgg/t3code')
try:
 t3['commit']=json.loads(get('https://api.github.com/repos/pingdotgg/t3code/commits/main'))['sha']
 t3['readme']=get('https://raw.githubusercontent.com/pingdotgg/t3code/'+t3['commit']+'/README.md')
 t3['error']='Recursive tree too large for bounded read; README and selected files retrieved separately.'
except Exception as e:t3['error']=str(e)
spec={
 'Altansar69/dofus-enhanced':['src/animations/animations.cpp','docs/GUIDE.md'],
 'Keka-Bron/JondoEmu':['Jondo.Unity.Server/Handlers/BankHandler.cs','Jondo.Unity.Protocol/Wire/ProtoSchema.cs'],
 'vDAKK/stakk-open-source':['package.json','lib/web-server.js'],
 'ix-xs/metamob.api':['package.json','docs/src/content/docs/basics/rate-limit.md'],
 'meakitfed/SuperDofus':['project.godot'],
 'Skkay/Ogrine-Rates_Server':['composer.json'],
 'Fronne/Dofus-Craft':['package.json'],
 'Unclerein/dofus-prospection':['dofustool/analysis/crafts.py','dofustool/web/server.py'],
 'Valentin-alix/BillBot':['pyproject.toml'],
 'totec448-spec/chat-on-steroids':['src/main/agents.ts','src/main/mcp/tools-core.ts','src/main/mcp/server.ts','docs/codex-desktop-bridge.md','package.json'],
 'pingdotgg/t3code':['package.json','apps/server/package.json'],
}
index={r['repo']:r for r in d['repositories']+d['bridge_repositories']}
def read_one(pair):
 repo,path=pair;sha=index[repo].get('commit');url=f'https://raw.githubusercontent.com/{repo}/{sha}/{path}'
 result={'repo':repo,'path':path,'url':url,'commit':sha}
 try:result['content']=get(url)
 except Exception as e:result['error']=str(e)
 return result
with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
 d['selected_files']=list(pool.map(read_one,[(r,p) for r,ps in spec.items() for p in ps]))
(OUT/'github-evidence.json').write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf8')
for r in d['selected_files']:print(r['repo'],r['path'],'ERROR '+r['error'] if 'error'in r else 'read '+str(len(r['content']))+' chars')
print('T3README',t3.get('readme','')[:2200].encode('ascii','backslashreplace').decode())
for r in d['selected_files']:
 if r['repo']=='totec448-spec/chat-on-steroids' and 'content'in r:
  print('COS',r['path'],r['content'][:1500].encode('ascii','backslashreplace').decode())
