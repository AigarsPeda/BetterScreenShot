const {chromium}=require('C:/Users/AigarsPeda/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('fs'),path=require('path'),{pathToFileURL}=require('url');
(async()=>{const browser=await chromium.launch({headless:true,executablePath:'C:/Users/AigarsPeda/.cache/puppeteer/chrome/win64-156.0.8078.47/chrome-win64/chrome.exe'});try{
const page=await browser.newPage({viewport:{width:1120,height:920},colorScheme:'light'});
await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
const inspection=await page.evaluate(()=>({links:[...document.querySelectorAll('a')].map(a=>a.getAttribute('href')),brokenImages:[...document.images].filter(i=>!i.complete||!i.naturalWidth).map(i=>i.src),missingAnchors:[...document.querySelectorAll('a[href^="#"]')].map(a=>a.getAttribute('href').slice(1)).filter(id=>!document.getElementById(id))}));
inspection.missingFiles=inspection.links.filter(s=>!s.startsWith('#')&&!/^https?:/.test(s)&&!fs.existsSync(path.join(__dirname,s)));
await page.screenshot({path:path.join(__dirname,'evidence','report-review.png')});
await page.setViewportSize({width:380,height:900});
inspection.mobileOverflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
fs.writeFileSync(path.join(__dirname,'evidence','report-checks.json'),JSON.stringify(inspection,null,2));
console.log(JSON.stringify(inspection,null,2));
if(inspection.brokenImages.length||inspection.missingAnchors.length||inspection.missingFiles.length||inspection.mobileOverflow)throw new Error('Report validation failed');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1)});
