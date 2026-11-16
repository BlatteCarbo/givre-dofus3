import concurrent.futures, json, pathlib, urllib.request, urllib.error

OUT = pathlib.Path('docs/codex/agent-program-20261026')
OUT.mkdir(parents=True, exist_ok=True)
HEADERS = {'User-Agent': 'Givre-backlog-research', 'Accept': 'application/vnd.github+json'}

def get(url, as_json=True):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=25) as response:
        data = response.read(6000000)
    return json.loads(data) if as_json else data.decode('utf-8', 'replace')

def inspect(repo):
    result = {'repo': repo, 'url': 'https://github.com/'+repo, 'retrieved': '2026-10-26'}
    try:
        meta = get('https://api.github.com/repos/'+repo)
        result.update(description=meta.get('description'), language=meta.get('language'),
                      license=meta.get('license'), archived=meta.get('archived'),
                      default_branch=meta.get('default_branch'), pushed_at=meta.get('pushed_at'))
        commit = get('https://api.github.com/repos/'+repo+'/commits/'+meta['default_branch'])
        result['commit'] = commit['sha']
        tree = get('https://api.github.com/repos/'+repo+'/git/trees/'+commit['sha']+'?recursive=1')
        files = [x['path'] for x in tree.get('tree', []) if x['type']=='blob']
        result['tree_truncated'] = tree.get('truncated', False)
        result['file_count'] = len(files)
        result['files'] = files[:5000]
        readmes = [x for x in files if x.lower() in ('readme.md', 'readme.rst', 'readme.txt', 'readme')]
        if readmes:
            result['readme_path'] = readmes[0]
            result['readme'] = get('https://raw.githubusercontent.com/'+repo+'/'+commit['sha']+'/'+readmes[0], False)[:30000]
    except Exception as exc:
        result['error'] = str(exc)
    return result

repos = ['Altansar69/dofus-enhanced', 'Keka-Bron/JondoEmu', 'vDAKK/stakk-open-source',
         'ix-xs/metamob.api', 'meakitfed/SuperDofus', 'Skkay/Ogrine-Rates_Server',
         'Fronne/Dofus-Craft', 'Unclerein/dofus-prospection', 'Valentin-alix/BillBot']
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    results = list(pool.map(inspect, repos))
searches = {}
for query in ('chat on steroids', 't3code in:name', 'chatonsteroids in:name'):
    try:
        searches[query] = get('https://api.github.com/search/repositories?q='+urllib.parse.quote(query)+'&per_page=5')
    except Exception as exc:
        searches[query] = {'error': str(exc)}
(OUT/'github-evidence.json').write_text(json.dumps({'repositories':results,'searches':searches},ensure_ascii=False,indent=2), encoding='utf-8')
for r in results:
    print(json.dumps({k:r.get(k) for k in ('repo','description','language','license','commit','file_count','error')},ensure_ascii=False))
for query, result in searches.items():
    print('SEARCH',query, json.dumps([(x['full_name'],x.get('description')) for x in result.get('items',[])], ensure_ascii=False), result.get('error',''))
