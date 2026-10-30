#!/usr/bin/env python3
import csv,json,os,pathlib,xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1];rows=[];seen=set()
cache=pathlib.Path(os.environ['NUGET_PACKAGES'])
for lock in [*root.glob('src/**/*.lock.json'),*root.glob('tests/**/*.lock.json')]:
 if lock.name!='packages.lock.json':continue
 for framework in json.loads(lock.read_text())['dependencies'].values():
  for name,info in framework.items():
   if 'resolved' not in info:continue
   key=(name.lower(),info['resolved'])
   if key in seen:continue
   seen.add(key);files=list((cache/key[0]/key[1]).glob('*.nuspec'));license='PENDING'
   if files:
    node=next((n for n in ET.parse(files[0]).iter() if n.tag.split('}')[-1]=='license'),None)
    if node is not None:license=(node.text or '')+' ('+node.get('type','expression')+')'
    elif name.lower()=='xunit.abstractions' and info['resolved']=='2.0.3':
     url=next((n.text for n in ET.parse(files[0]).iter() if n.tag.split('}')[-1]=='licenseUrl'),None)
     evidence=root/'docs/evidence/xunit-official-license.txt'
     if url=='https://raw.githubusercontent.com/xunit/xunit/master/license.txt' and evidence.exists() and 'Apache License, Version 2.0' in evidence.read_text():
      license='Apache-2.0 (publisher licenseUrl; fetched evidence retained)'
   rows.append(['NuGet',name,key[1],license])
web=root/'src/Clients/Web';lock=json.loads((web/'package-lock.json').read_text())
for path,info in lock['packages'].items():
 if not path:continue
 package=web/path/'package.json'
 if package.exists():
  p=json.loads(package.read_text());rows.append(['npm',p['name'],p['version'],str(p.get('license','PENDING'))])
out=root/'docs/evidence/dependency-licenses.csv'
with out.open('w') as f:
 w=csv.writer(f);w.writerow(['ecosystem','package','version','declaredLicense']);w.writerows(sorted(rows))
print('Recorded',len(rows),'installed package licenses; unknown:',sum('PENDING' in r[3] for r in rows))
