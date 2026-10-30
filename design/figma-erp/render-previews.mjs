import { chromium } from '../../src/Clients/Web/node_modules/@playwright/test/index.mjs';
import { readdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const browser=await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
try{const page=await browser.newPage({viewport:{width:1440,height:1024},deviceScaleFactor:1});for(const name of (await readdir(new URL('.',import.meta.url))).filter(n=>n.endsWith('.svg'))){const url=new URL(name,import.meta.url);await page.setContent(await readFile(url,'utf8'));await page.screenshot({path:resolve(new URL('.',import.meta.url).pathname,name.replace('.svg','.png'))});console.log(name.replace('.svg','.png'))}}finally{await browser.close()}
