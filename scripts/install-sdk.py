#!/usr/bin/env python3
"""Pinned official SDK, HTTPS + published SHA512, extracted only to the writable tool cache."""
import hashlib,json,os,pathlib,tarfile,urllib.request
version='10.0.402';cache=pathlib.Path(os.environ['POC_TOOLS']);cache.mkdir(parents=True,exist_ok=True)
with urllib.request.urlopen('https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json',timeout=60) as r:metadata=json.load(r)
artifact=next(f for release in metadata['releases'] for sdk in release.get('sdks',[]) if sdk['version']==version for f in sdk['files'] if f['rid']=='linux-x64' and f['name'].endswith('.tar.gz'))
archive=cache/'dotnet-sdk.tar.gz'
if not archive.exists():urllib.request.urlretrieve(artifact['url'],archive)
with archive.open('rb') as f:actual=hashlib.file_digest(f,'sha512').hexdigest()
if actual!=artifact['hash']:raise SystemExit('SDK SHA512 mismatch; installation refused')
destination=cache/'dotnet';destination.mkdir(exist_ok=True)
with tarfile.open(archive) as t:t.extractall(destination,filter='data')
print('Verified .NET SDK '+version+' installed.')
