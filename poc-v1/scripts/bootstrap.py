#!/usr/bin/env python3
"""Install only verified disposable PoC tooling into a writable cache; no system changes."""
import hashlib,json,os,pathlib,subprocess,tarfile,urllib.request
ROOT=pathlib.Path(__file__).resolve().parents[1]
TOOLS=pathlib.Path(os.environ.get('POC_TOOLS','/workspace/erp-poc-v1/tools'))
SDK='10.0.402'
TOOLS.mkdir(parents=True,exist_ok=True)
metadata=urllib.request.urlopen('https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json').read()
x=json.loads(metadata)
f=next(f for r in x['releases'] for s in r.get('sdks',[]) if s['version']==SDK for f in s['files'] if f['rid']=='linux-x64' and f['name'].endswith('.tar.gz'))
archive=TOOLS/'dotnet-sdk.tar.gz'
if not archive.exists():urllib.request.urlretrieve(f['url'],archive)
actual=hashlib.file_digest(archive.open('rb'),'sha512').hexdigest()
if actual!=f['hash']:raise SystemExit('SDK checksum mismatch; do not use archive')
if not (TOOLS/'dotnet/dotnet').exists():
 (TOOLS/'dotnet').mkdir(exist_ok=True)
 with tarfile.open(archive) as t:t.extractall(TOOLS/'dotnet',filter='data')
(ROOT/'evidence').mkdir(exist_ok=True)
(ROOT/'evidence/dotnet-releases.json').write_bytes(metadata)
(ROOT/'evidence/sdk-checksum.json').write_text(json.dumps({'url':f['url'],'expected_sha512':f['hash'],'actual_sha512':actual,'verified':True},indent=2))
print(subprocess.check_output([str(TOOLS/'dotnet/dotnet'),'--version'],text=True).strip())
for cmd in [['node','--version'],['npm','--version'],['docker','version','--format','{{.Server.Version}}'],['chromium','--version']]:
 print(subprocess.check_output(cmd,text=True).strip())
