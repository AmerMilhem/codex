#!/usr/bin/env python3
"""Record resolved graph, official manifests, licenses and source hashes. Not a legal opinion."""
import csv,hashlib,json,os,pathlib,subprocess,time,urllib.request,xml.etree.ElementTree as ET
ROOT=pathlib.Path(__file__).resolve().parents[1];E=ROOT/'evidence';E.mkdir(exist_ok=True)
TOOLS=pathlib.Path(os.environ.get('POC_TOOLS','/workspace/erp-poc-v1/tools'))
sources={
 'angular-compiler':'https://registry.npmjs.org/@angular/compiler-cli/22.2.3',
 'angular-core':'https://registry.npmjs.org/@angular/core/22.2.3',
 'angular-cli':'https://registry.npmjs.org/@angular/cli/22.2.3',
 'typescript':'https://registry.npmjs.org/typescript/6.0.3',
 'playwright':'https://registry.npmjs.org/@playwright/test/1.64.1',
 'node-schedule':'https://raw.githubusercontent.com/nodejs/Release/main/schedule.json',
 'angular-version-policy':'https://raw.githubusercontent.com/angular/angular/main/adev/src/content/reference/versions.md',
 'dotnet-policy':'https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/README.md',
 'postgres-tag':'https://raw.githubusercontent.com/postgres/postgres/REL_18_2/configure.ac',
 'postgres-license':'https://raw.githubusercontent.com/postgres/postgres/REL_18_2/COPYRIGHT',
 'flutter-release-archive':'https://storage.googleapis.com/flutter_infra_release/flutter/releases/releases_linux.json',
 'flutter-license':'https://raw.githubusercontent.com/flutter/flutter/stable/LICENSE',
 'dart-license':'https://raw.githubusercontent.com/dart-lang/sdk/main/LICENSE',
 'testcontainers-license':'https://raw.githubusercontent.com/testcontainers/testcontainers-dotnet/develop/LICENSE',
 'node-license':'https://raw.githubusercontent.com/nodejs/node/v24.19.0/LICENSE',
}
manifest=[]
for name,url in sources.items():
 try:
  with urllib.request.urlopen(url,timeout=25) as r:data=r.read();code=r.status
  path=E/(name+'.source');path.write_bytes(data)
  manifest.append({'name':name,'url':url,'status':code,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'file':path.name})
 except Exception as ex:manifest.append({'name':name,'url':url,'error':str(ex)})
(E/'official-source-manifest.json').write_text(json.dumps({'utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'sources':manifest},indent=2))
assets=json.loads((ROOT/'src/obj/project.assets.json').read_text());licenses=[]
for name,info in assets['libraries'].items():
 if info['type']!='package':continue
 package,version=name.rsplit('/',1);folder=TOOLS/'nuget'/package.lower()/version
 nuspec=next(folder.glob('*.nuspec'),None);expression='PENDING';kind='unknown';url=''
 if nuspec:
  root=ET.parse(nuspec).getroot();meta=next(x for x in root.iter() if x.tag.split('}')[-1]=='metadata')
  fields={x.tag.split('}')[-1]:x for x in meta};lic=fields.get('license')
  if lic is not None:expression=lic.text or '';kind=lic.get('type','')
  if fields.get('licenseUrl') is not None:url=fields['licenseUrl'].text or ''
 licenses.append({'ecosystem':'NuGet','package':package,'version':version,'license':expression,'kind':kind,'source':f'https://api.nuget.org/v3-flatcontainer/{package.lower()}/{version}/{package.lower()}.nuspec','licenseUrl':url})
lock=json.loads((ROOT/'web/package-lock.json').read_text())
for path,entry in lock['packages'].items():
 if not path:continue
 p=ROOT/'web'/path/'package.json'
 if not p.exists():continue
 pkg=json.loads(p.read_text());lic=pkg.get('license','PENDING')
 if isinstance(lic,dict):lic=lic.get('type','PENDING')
 licenses.append({'ecosystem':'npm','package':pkg['name'],'version':pkg['version'],'license':lic,'kind':'package metadata','source':entry.get('resolved',''),'licenseUrl':''})
with (E/'resolved-license-matrix.csv').open('w') as f:
 w=csv.DictWriter(f,fieldnames=['ecosystem','package','version','license','kind','source','licenseUrl']);w.writeheader();w.writerows(licenses)
unknown=[x for x in licenses if x['license']=='PENDING' or x['kind']=='file']
(E/'license-review.json').write_text(json.dumps({'resolvedPackages':len(licenses),'nuget':sum(x['ecosystem']=='NuGet' for x in licenses),'npm':sum(x['ecosystem']=='npm' for x in licenses),'manualFileOrMissingReview':unknown,'meaning':'Declared licenses recorded; notices, binary distribution and final counsel review remain deployment obligations'},indent=2))
runtime={}
for name,cmd in {'dotnet':[str(TOOLS/'dotnet/dotnet'),'--info'],'node':['node','--version'],'npm':['npm','--version'],'chromium':['chromium','--version'],'docker':['docker','version','--format','{{.Server.Version}}'],'postgres':['docker','exec','erp-poc-v1-pg','psql','-U','postgres','-d','poc','-Atc','SHOW server_version']}.items():
 runtime[name]=subprocess.check_output(cmd,text=True).strip()
(E/'runtime-versions.json').write_text(json.dumps(runtime,indent=2))
print(json.dumps({'sources':len(manifest),'resolvedPackages':len(licenses),'manualReview':len(unknown)},indent=2))
