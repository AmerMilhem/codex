import {chromium} from '@playwright/test';
import {writeFile,mkdir} from 'node:fs/promises';
const evidence=new URL('../evidence/',import.meta.url);await mkdir(evidence,{recursive:true});
const browser=await chromium.launch({executablePath:process.env.POC_CHROMIUM||'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
const page=await browser.newPage({viewport:{width:1280,height:900}});const errors=[];page.on('pageerror',e=>errors.push(e.message));const checks=[];
function check(name,value,detail){checks.push({name,status:value?'PASS':'FAIL',detail});if(!value)throw new Error(name);}
try{
 const before=performance.now();await page.goto('http://127.0.0.1:4207');await page.locator('tbody tr').last().waitFor();const renderMs=performance.now()-before;
 check('100 editable rows',await page.locator('tbody tr').count()===100,{rows:100,renderMs});
 check('Arabic RTL',await page.locator('html').getAttribute('dir')==='rtl'&&await page.locator('h1').textContent()==='تجربة جدول تقني');
 check('restricted column absent',await page.locator('.cost').count()===0);
 const q=page.getByRole('spinbutton',{name:'Quantity 1',exact:true});await q.focus();await q.press('ArrowDown');check('keyboard next row',await page.getByRole('spinbutton',{name:'Quantity 2',exact:true}).evaluate(x=>x===document.activeElement));
 await q.fill('-1');await q.blur();await page.getByRole('alert').first().waitFor();check('reactive invalid quantity blocks submit',await page.locator('#validate').isDisabled()&&await page.locator('#validity').textContent()==='INVALID');
 await q.fill('2.5');await q.blur();await page.waitForFunction(()=>document.querySelector('#validity').textContent==='VALID');check('reactive correction',await page.locator('#validate').isEnabled());
 await page.locator('#language').click();check('English LTR',await page.locator('html').getAttribute('dir')==='ltr'&&await page.locator('h1').textContent()==='Technical grid experiment');
 await page.locator('#permission').click();await page.waitForFunction(()=>document.querySelectorAll('.cost').length===100);check('permission visibility fixture',await page.locator('.cost').count()===100);
 check('inputs accessible names',await page.locator('tbody input:not([aria-label])').count()===0);check('no horizontal overflow at 1280',await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 await page.screenshot({path:new URL('angular-grid.png',evidence).pathname,fullPage:false});check('no browser runtime errors',errors.length===0,errors);
}finally{await writeFile(new URL('angular-browser.json',evidence),JSON.stringify({utc:new Date().toISOString(),browser:await browser.version(),executable:'/usr/bin/chromium',playwright:'1.64.1',checks,errors,limitation:'Installed Chromium, not Playwright managed bundled revision; screen-reader audit and production browser matrix pending'},null,2));await browser.close();}
