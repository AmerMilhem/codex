import json,pathlib,sys,xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1]
assert json.loads((root/'global.json').read_text())['sdk']['version']=='10.0.402'
web=json.loads((root/'src/Clients/Web/package.json').read_text())
assert web['devDependencies']['typescript']=='6.0.3'
assert all(v=='22.2.3' for k,v in {**web['dependencies'],**web['devDependencies']}.items() if k.startswith('@angular/'))
for section in ['dependencies','devDependencies']:
 assert all(not any(c in version for c in '^~*<>') and version!='latest' for version in web[section].values())
for project in [*root.glob('src/**/*.csproj'),*root.glob('tests/**/*.csproj')]:
 lock=project.parent/'packages.lock.json';assert lock.exists(),str(lock)
 for framework in json.loads(lock.read_text())['dependencies'].values():
  for name,entry in framework.items():
   if name.lower().startswith('microsoft.entityframeworkcore'):assert entry.get('resolved')=='10.0.13',(project,name,entry)
print('Exact pins, lock coverage and EF alignment verified.')
