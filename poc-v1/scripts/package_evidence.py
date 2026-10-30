#!/usr/bin/env python3
import hashlib,json,pathlib,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[1]
excluded={'node_modules','bin','obj','.angular','dist','runs','object-fixture','object-backup','object-restored'}
files=[p for p in ROOT.rglob('*') if p.is_file() and not (set(p.relative_to(ROOT).parts)&excluded) and p.name!='artifact-manifest.json']
manifest={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}
out=ROOT/'evidence/artifact-manifest.json';out.write_text(json.dumps(manifest,indent=2))
archive=pathlib.Path('/workspace/erp-poc-v1/ERP-PoC-V1-Evidence.zip')
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as z:
 for p in sorted(files+[out]):z.write(p,'poc-v1/'+str(p.relative_to(ROOT)))
print(json.dumps({'archive':str(archive),'files':len(files)+1,'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest()},indent=2))
