const {chromium}=require('C:/Users/AigarsPeda/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('fs');
const path=require('path');
const {pathToFileURL}=require('url');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:'C:/Users/AigarsPeda/.cache/puppeteer/chrome/win64-156.0.8078.47/chrome-win64/chrome.exe'});
 const page=await browser.newPage({viewport:{width:1060,height:900}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(pathToFileURL(path.join(__dirname,'preview.html')).href);
 await page.waitForTimeout(1500);
 const frame=page.frames().find(f=>f!==page.mainFrame());
 if(!frame) throw new Error('Preview iframe not found');
 await frame.waitForSelector('#bss-product .editor-top');
 const results=[];const interactions=[];
 for(const width of [1024,760,620,380,320]){
  await page.setViewportSize({width,height:1000});
  for(const theme of ['light','dark']){
   await frame.locator('#review-theme').selectOption(theme);
   for(const screen of ['editor','launcher','preview','scroll','record','viewer','settings']){
    await frame.locator('#review-screen').selectOption(screen);
    results.push(await frame.evaluate(({width,theme,screen})=>{
     const app=document.querySelector('#bss-product');const r=app.getBoundingClientRect();
     const overflow=[...app.querySelectorAll('*')].filter(el=>{const a=el.getBoundingClientRect();return a.width>0&&(a.right>r.right+2||a.left<r.left-2)}).map(el=>el.tagName+':'+(el.getAttribute('aria-label')||el.textContent).slice(0,65));
     return {width,theme,screen,appWidth:r.width,overflow,documentOverflow:document.documentElement.scrollWidth>innerWidth};
    },{width,theme,screen}));
   }
  }
 }
 for(const width of [1024,620,320]){
  await page.setViewportSize({width,height:1000});
  for(const theme of ['light','dark']){
   await frame.locator('#review-theme').selectOption(theme);
   for(const scenario of ['text','error','empty','loading','conflict','gap']){
    await frame.locator('#review-state').selectOption(scenario);
    results.push(await frame.evaluate(({width,theme,scenario})=>{
     const app=document.querySelector('#bss-product');const r=app.getBoundingClientRect();
     const overflow=[...app.querySelectorAll('*')].filter(el=>{const a=el.getBoundingClientRect();return a.width>0&&(a.right>r.right+2||a.left<r.left-2)}).map(el=>el.tagName+':'+el.textContent.slice(0,65));
     return {width,theme,screen:scenario,appWidth:r.width,overflow,documentOverflow:document.documentElement.scrollWidth>innerWidth};
    },{width,theme,scenario}));
   }
  }
 }
 async function check(name,test){if(!await test())throw new Error(name);interactions.push(name);}
 await page.setViewportSize({width:1060,height:950});
 await frame.locator('#review-state').selectOption('error');
 await frame.locator('[data-action=retry]').click();
 await check('OCR retry returns readable text',async()=>await frame.locator('textarea').isVisible());
 await frame.getByRole('button',{name:'Close text panel',exact:true}).click();
 await check('Closing text returns focus to Extract text',()=>frame.evaluate(()=>document.activeElement.dataset.action==='ocr'));
 await frame.locator('[data-action=close]').click();
 await check('Unsaved close opens guarded dialog',async()=>await frame.locator('dialog').isVisible());
 await check('Safe dialog action receives focus',()=>frame.evaluate(()=>document.activeElement.dataset.action==='dialog-cancel'));
 await page.keyboard.press('Escape');
 await check('Escape restores close-button focus',()=>frame.evaluate(()=>document.activeElement.dataset.action==='close'));
 await frame.locator('#review-state').selectOption('conflict');
 await frame.locator('[data-action=settings-save]').click();
 await check('Settings validation focuses invalid field',()=>frame.evaluate(()=>document.activeElement.id==='area-shortcut'&&document.activeElement.getAttribute('aria-invalid')==='true'));
 await frame.locator('#area-shortcut').fill('Ctrl + Shift + B');
 await frame.locator('[data-action=settings-save]').click();
 await check('Valid settings returns to launcher',()=>frame.locator('.launcher-list').isVisible());
 await frame.locator('#review-screen').selectOption('record');
 await frame.locator('[data-action=start-record]').first().click();
 await frame.getByRole('button',{name:'Discard…',exact:true}).click();
 await check('Active recording discard requires confirmation',()=>frame.locator('dialog').isVisible());
 await frame.locator('[data-action=dialog-cancel]').click();
 await frame.locator('[data-action=stop-record]').click();
 await check('Stop and keep opens recording preview',()=>frame.locator('.playback').isVisible());
 await page.setViewportSize({width:1060,height:950});
 await frame.locator('#review-state').selectOption('normal');
 await frame.locator('#review-screen').selectOption('editor');
 await frame.locator('#review-theme').selectOption('light');
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-editor-light.png')});
 await frame.locator('[data-action=ocr]').click();
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-ocr-light.png')});
 await frame.locator('[data-action=copy-text]').click();
 if(!await frame.locator('#bss-status').textContent().then(t=>t.includes('Copied')))throw new Error('Copy feedback missing');
 await page.setViewportSize({width:652,height:950});
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-ocr-compact.png')});
 await frame.locator('#review-theme').selectOption('dark');
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-ocr-dark.png')});
 await frame.locator('#review-state').selectOption('error');
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-ocr-error.png')});
 await frame.locator('#review-state').selectOption('conflict');
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-settings.png')});
 await page.setViewportSize({width:652,height:950});
 await frame.locator('#review-state').selectOption('normal');
 await frame.locator('#review-screen').selectOption('editor');
 const compactHeights=[];
 for(const mode of ['image','text']){
  if(mode==='text')await frame.locator('[data-action=ocr]').click();
  const bounds=await frame.locator('#bss-product').boundingBox();
  compactHeights.push({mode,width:bounds.width,height:bounds.height});
 }
 console.log('Compact geometry:',JSON.stringify(compactHeights));
 await check('620-DIP prototype fits within 450-DIP height in image and text modes',async()=>compactHeights.every(b=>b.height<=450&&Math.abs(b.width-620)<2));
 await frame.locator('#review-state').selectOption('normal');
 await frame.locator('#review-screen').selectOption('settings');
 await frame.locator('#area-shortcut').fill('');
 await frame.locator('#full-shortcut').fill('');
 await frame.locator('[data-action=settings-save]').click();
 await check('Cleared shortcuts remain valid',()=>frame.locator('.launcher-list').isVisible());
 await frame.locator('#review-screen').selectOption('preview');
 await frame.locator('[data-action=next-capture]').click();
 await check('Pending preview navigation updates selection',()=>frame.locator('.pending-nav').textContent().then(t=>t.includes('2 of 2')));
 await frame.locator('#review-screen').selectOption('editor');
 await frame.locator('[data-tool=Arrow]').focus();await page.keyboard.press('p');
 await check('Tool shortcut exposes selection beyond color',()=>frame.locator('[data-tool=Pen]').getAttribute('aria-pressed').then(v=>v==='true'));
 await page.emulateMedia({forcedColors:'active',reducedMotion:'reduce'});
 await frame.locator('[data-tool=Pen]').focus();
 await check('Forced-color focus remains visible',()=>frame.evaluate(()=>getComputedStyle(document.activeElement).outlineStyle!=='none'));
 await frame.locator('#bss-review').screenshot({path:path.join(__dirname,'evidence','proposal-high-contrast.png')});
 const failures=results.filter(r=>r.overflow.length||r.documentOverflow);
 fs.writeFileSync(path.join(__dirname,'evidence','layout-checks.json'),JSON.stringify({errors,results,interactions,compactHeights},null,2));
 console.log(JSON.stringify({errors,cases:results.length,interactions,compactHeights,failures},null,2));
 if(errors.length||failures.length) process.exitCode=1;
 await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
