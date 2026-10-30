import http.server,pathlib,os,urllib.request,urllib.error,urllib.parse
root=pathlib.Path(__file__).resolve().parents[1]/'src/Clients/Web/dist/web/browser'
class Handler(http.server.SimpleHTTPRequestHandler):
 def __init__(self,*args,**kwargs):super().__init__(*args,directory=str(root),**kwargs)
 def do_GET(self):
  if self.path.startswith('/api/') and os.environ.get('ERP_TEST_API_ORIGIN'):return self.proxy()
  if not (root/self.path.lstrip('/').split('?')[0]).is_file():self.path='/index.html'
  super().do_GET()
 def do_POST(self):self.proxy()
 def do_PUT(self):self.proxy()
 def proxy(self):
  origin=os.environ.get('ERP_TEST_API_ORIGIN','');parsed=urllib.parse.urlsplit(origin)
  if parsed.scheme!='http' or parsed.hostname!='127.0.0.1' or parsed.path or not self.path.startswith('/api/'):
   self.send_error(404);return
  length=int(self.headers.get('Content-Length','0'))
  if length>65536:self.send_error(413);return
  headers={k:v for k,v in self.headers.items() if k.lower() not in ['host','connection','content-length']}
  request=urllib.request.Request(origin+self.path,data=self.rfile.read(length) if length else None,headers=headers,method=self.command)
  try:response=urllib.request.urlopen(request,timeout=30)
  except urllib.error.HTTPError as error:response=error
  with response:
   body=response.read();self.send_response(response.status)
   for key,value in response.headers.items():
    if key.lower() not in ['transfer-encoding','connection','content-length']:self.send_header(key,value)
   self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
 def end_headers(self):
  self.send_header('X-Content-Type-Options','nosniff');self.send_header('X-Frame-Options','DENY');self.send_header('Referrer-Policy','no-referrer')
  self.send_header('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'")
  super().end_headers()
http.server.ThreadingHTTPServer(('127.0.0.1',int(os.environ.get('ERP_WEB_PORT','4208'))),Handler).serve_forever()
