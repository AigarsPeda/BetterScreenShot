const {chromium}=require('C:/Users/AigarsPeda/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('fs'),path=require('path'),{pathToFileURL}=require('url');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:'C:/Users/AigarsPeda/.cache/puppeteer/chrome/win64-156.0.8078.47/chrome-win64/chrome.exe'});
 try{
 const page=await browser.newPage({viewport:{width:1056,height:850}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(pathToFileURL(path.join(__dirname,'preview.html')).href);
 const frame=page.frames().find(f=>f!==page.mainFrame());await frame.waitForSelector('.editor-top');
 const results=[];
 for(const width of [1024,810,760,620,380,320]){
  await page.setViewportSize({width:width+32,height:1000});
  for(const theme of ['light','dark']){
   await frame.locator('#review-theme').selectOption(theme);
   await frame.locator('#review-state').selectOption('normal');await frame.locator('#review-screen').selectOption('editor');
   await frame.locator('[data-tool=Arrow]').click();
   for(const mode of ['default','properties','text']){
    if(mode==='properties'&&!await frame.locator('.floating-properties').isVisible())await frame.locator('[data-action=properties]').click();
    if(mode==='text')await frame.locator('[data-action=ocr]').click();
    results.push(await frame.evaluate(({width,theme,mode})=>{
     const app=document.querySelector('#bss-product'),r=app.getBoundingClientRect();
     const overflow=[...app.querySelectorAll('*')].filter(el=>{const a=el.getBoundingClientRect();return a.width&&(a.right>r.right+2||a.left<r.left-2||a.bottom>r.bottom+2)}).map(el=>el.className||el.tagName);
     const rect=s=>{const el=app.querySelector(s);if(!el||getComputedStyle(el).display==='none'||getComputedStyle(el).visibility==='hidden')return null;return el.getBoundingClientRect();};
     const intersects=(a,b)=>!!a&&!!b&&a.left<b.right&&a.right>b.left&&a.top<b.bottom&&a.bottom>b.top;
     const overlaps=[];
     for(const[a,b]of [['.tools','.exports'],['.tools','.editor-menu'],['.floating-properties','.canvas-footer']])if(intersects(rect(a),rect(b)))overlaps.push(a+' / '+b);
     if(width>760&&intersects(rect('.floating-properties'),rect('.sample-image')))overlaps.push('properties / image');
     return{width,theme,mode,height:r.height,overflow,overlaps};
    },{width,theme,mode}));
   }
  }
 }
 await page.setViewportSize({width:1056,height:850});await frame.locator('#review-state').selectOption('normal');await frame.locator('[data-tool=Arrow]').click();
 await frame.locator('#review-theme').selectOption('dark');await frame.locator('#bss-product').screenshot({path:path.join(__dirname,'evidence','floating-editor-dark.png')});
 await frame.locator('#review-theme').selectOption('light');await frame.locator('#bss-product').screenshot({path:path.join(__dirname,'evidence','floating-editor-light.png')});
 const checks=[];async function check(name,promise){if(!await promise)throw new Error(name);checks.push(name);}
 await frame.locator('[data-color=Blue]').click();await check('Color selection changes',frame.locator('[data-color=Blue]').getAttribute('aria-pressed').then(v=>v==='true'));
 await frame.locator('[data-stroke=Thick]').click();await check('Stroke selection changes',frame.locator('[data-stroke=Thick]').getAttribute('aria-pressed').then(v=>v==='true'));
 await frame.locator('[data-tool=Text]').click();await check('Text tool shows text size',frame.locator('[data-text-size="28"]').isVisible());
 await frame.locator('[data-tool=Select]').click();await check('Select hides unnecessary properties',frame.locator('.floating-properties').count().then(n=>n===0));
 await page.setViewportSize({width:652,height:850});await frame.locator('[data-tool=Arrow]').click();await check('Compact properties collapsed by default',frame.locator('.floating-properties').isVisible().then(v=>!v));
 await frame.locator('#bss-product').screenshot({path:path.join(__dirname,'evidence','floating-editor-compact.png')});
 await frame.locator('[data-action=properties]').click();await check('Compact properties open on request',frame.locator('.floating-properties').isVisible());
 await frame.locator('#bss-product').screenshot({path:path.join(__dirname,'evidence','floating-properties-compact.png')});
 await frame.locator('[data-action=properties-close]').click();await check('Closing properties returns focus',frame.evaluate(()=>document.activeElement.dataset.action==='properties'));
 const failures=results.filter(r=>r.overflow.length||r.overlaps.length);
 fs.writeFileSync(path.join(__dirname,'evidence','floating-checks.json'),JSON.stringify({errors,results,checks,failures},null,2));
 console.log(JSON.stringify({errors,cases:results.length,checks,failures},null,2));if(errors.length||failures.length)process.exitCode=1;
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exit(1)});
