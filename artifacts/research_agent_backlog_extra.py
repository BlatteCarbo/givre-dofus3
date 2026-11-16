import concurrent.futures, json, pathlib, urllib.request
OUT=pathlib.Path('docs/codex/agent-program-20261026')
headers={'User-Agent':'Givre-backlog-research','Accept':'application/vnd.github+json'}
def get(url, json_result=True):
    with urllib.request.urlopen(urllib.request.Request(url,headers=headers), timeout=25) as r:
        b=r.read(8000000)
    return json.loads(b) if json_result else b.decode('utf-8','replace')
def inspect(repo):
    r={'repo':repo,'url':'https://github.com/'+repo,'retrieved':'2026-10-26'}
    try:
        m=get('https://api.github.com/repos/'+repo)
        sha=get('https://api.github.com/repos/'+repo+'/commits/'+m['default_branch'])['sha']
        tree=get('https://api.github.com/repos/'+repo+'/git/trees/'+sha+'?recursive=1')
        r.update(commit=sha,description=m.get('description'),license=m.get('license'),
                 files=[x['path'] for x in tree.get('tree',[]) if x['type']=='blob'],tree_truncated=tree.get('truncated'))
        rms=[p for p in r['files'] if p.lower()=='readme.md']
        if rms:r['readme']=get('https://raw.githubusercontent.com/'+repo+'/'+sha+'/'+rms[0],False)[:40000]
    except Exception as e:r['error']=str(e)
    return r
data=json.loads((OUT/'github-evidence.json').read_text(encoding='utf8'))
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    data['bridge_repositories']=list(pool.map(inspect,['totec448-spec/chat-on-steroids','pingdotgg/t3code']))
(OUT/'github-evidence.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf8')
for r in data['bridge_repositories']:
    print(r['repo'],r.get('commit'),r.get('error'),r.get('license'))
    print('README',r.get('readme','')[:4200].encode('ascii','backslashreplace').decode())
    print('FILES',json.dumps([p for p in r.get('files',[]) if any(t in p.lower() for t in ('mcp','extension','bridge','agent','orchestrat','manifest','native-messag'))][:90]))
