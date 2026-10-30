#!/usr/bin/env python3
import json,pathlib,urllib.error,urllib.request
root=pathlib.Path(__file__).resolve().parents[1]
token=json.loads((root/'.local/settings.json').read_text())['token']
base='http://127.0.0.1:5080/api/v1/tenants/'
tenant='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';company='11111111-1111-4111-8111-111111111111'
def get(t,c,auth=True):
 req=urllib.request.Request(base+t+'/companies/'+c+'/context',headers={'Authorization':'Bearer '+token,'X-Correlation-ID':'local-foundation-smoke'} if auth else {})
 try:
  with urllib.request.urlopen(req,timeout=5) as response:return response.status,json.load(response),dict(response.headers)
 except urllib.error.HTTPError as e:return e.code,json.load(e),dict(e.headers)
status,body,headers=get(tenant,company);assert status==200 and body['tenantId']==tenant and body['companyId']==company
assert headers['X-Correlation-ID']=='local-foundation-smoke' and headers['X-Content-Type-Options']=='nosniff'
assert get(tenant,company,False)[0]==401
assert get('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb','33333333-3333-4333-8333-333333333333')[0]==403
assert get(tenant,'22222222-2222-4222-8222-222222222222')[0]==403
print(json.dumps({'context':200,'anonymous':401,'crossTenant':403,'crossCompany':403,'correlation':'propagated','secretsPrinted':False}))
