"""Compare recovered binding surfaces against source types; names are evidence, not recovered bodies."""
from pathlib import Path
import argparse,json,re
parser=argparse.ArgumentParser()
parser.add_argument('--evidence',type=Path,default=Path(r'%USERPROFILE%\Downloads\GivreClone\analysis'))
args=parser.parse_args();root=Path(__file__).resolve().parents[1]
types={}
for project in ['GivreClone.App','GivreClone.Engine','GivreClone.Protocol']:
 for path in (root/project).rglob('*.cs'):
  if {'bin','obj'} & set(path.parts):continue
  text=path.read_text(encoding='utf-8-sig',errors='replace')
  for name in re.findall(r'\b(?:class|struct|interface|enum|record(?:\s+struct)?)\s+(\w+)',text):
   types.setdefault(name,set()).add(str(path.relative_to(root)))
surfaces={}
pattern=re.compile(r'GivreCloneBot\.App\.(ViewModels|Services)\.([A-Za-z_]\w*),GivreCloneBot\.([A-Za-z_]\w*)!(?:Getter|Property)')
for line in (args.evidence/'GivreCloneBot.strings.ascii.jsonl').open(encoding='utf-8'):
 row=json.loads(line)
 for area,owner,member in pattern.findall(row['text']):
  entry=surfaces.setdefault(area+'.'+owner,{'members':set(),'rvas':set(),'source_files':sorted(types.get(owner,[]))})
  entry['members'].add(member);entry['rvas'].add(row['rva'])
for entry in surfaces.values():
 entry['members']=sorted(entry['members']);entry['rvas']=sorted(entry['rvas'],key=lambda a:int(a,16))
result={'evidence_sha256':json.loads((args.evidence/'memory-identity.json').read_text())['sha256'],
 'source_boundary':['GivreClone.App','GivreClone.Engine','GivreClone.Protocol'],
 'limitations':['Binding names do not recover method bodies, types, message IDs or runtime behavior.',
 'A missing source type name is a comparison candidate, not proof of missing equivalent behavior.',
 'Matching a type name does not prove member-by-member parity.'],
 'surface_count':len(surfaces),'surfaces':dict(sorted(surfaces.items()))}
out=root/'docs'/'recovery134';out.mkdir(exist_ok=True)
(out/'binding-comparison.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
missing=[name for name,item in surfaces.items() if not item['source_files']]
print(json.dumps({'binding_types':len(surfaces),'type_name_candidates_without_source':missing},ensure_ascii=False))
