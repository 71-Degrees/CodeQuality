/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>The feature map's single self-contained page: summary, filters and one row per page.</summary>
internal static class FeatureMapPage
{
    public const string Template = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>/*TITLE*/ - feature map</title>
<style>
:root{--bg:#f6f5f1;--card:#fff;--ink:#1d2030;--dim:#5d6378;--line:#e3e1d8;--accent:#b9791f;--bad:#b3261e;--warn:#a65f00;--ok:#1b7a43;--chip:#efece3}
@media (prefers-color-scheme:dark){:root{--bg:#0f1220;--card:#181c30;--ink:#eceefb;--dim:#9aa1c4;--line:#2a2f4a;--accent:#e0a64b;--bad:#ff8a80;--warn:#ffb74d;--ok:#6fd49a;--chip:#232845}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Segoe UI",system-ui,sans-serif}
main{max-width:1280px;margin:0 auto;padding:20px 16px 60px}
h1{font-size:1.6rem;margin:.2rem 0}h2{font-size:1.1rem;margin:1.6rem 0 .4rem}.dim{color:var(--dim)}a{color:var(--accent)}
.hero{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px;margin:14px 0}
.big{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px 16px}.big .n{font-size:2.4rem;font-weight:700;line-height:1;font-variant-numeric:tabular-nums}.big .l{font-weight:600;margin-top:.3rem}.big .s{color:var(--dim);font-size:.85rem}
.good{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}
.controls{display:flex;flex-wrap:wrap;gap:8px;margin:10px 0}input,select{font:inherit;padding:5px 8px;border-radius:6px;border:1px solid var(--line);background:var(--card);color:var(--ink)}
.scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);font-size:.9rem}
th,td{padding:6px 10px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top}th{background:var(--chip);cursor:pointer;white-space:nowrap;user-select:none}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}.mono{font:13px Consolas,monospace}
.tag{display:inline-block;padding:1px 8px;border-radius:99px;font-size:.75rem;background:var(--chip);white-space:nowrap;margin:0 4px 2px 0}
.tag.browser{color:var(--ok)}.tag.component{color:var(--accent)}details summary{cursor:pointer}details ul{margin:4px 0 0 16px;padding:0}
</style></head><body><main>
<h1>/*TITLE*/ - feature map</h1>
<div class="dim" id="meta"></div>
<section id="fn-section" hidden>
<h2>Functions</h2>
<div class="dim">What a person can do, in their words. Steps and test lists are curated in quality/features.json and checked against the code every time: a test that does not exist is flagged, and so is a page that belongs to no function.</div>
<div class="hero" id="fn-hero"></div>
<div id="fn-warn"></div>
<div class="controls">
  <input id="fq" type="search" placeholder="Filter functions" aria-label="Filter functions">
  <select id="farea" aria-label="Function area"></select>
  <select id="fwho" aria-label="Who"></select>
  <select id="fstatus" aria-label="Function test status"><option value="">Any test status</option><option value="none">Not covered</option><option value="weak">Unit or component tests only</option><option value="browser">Has a browser test</option></select>
</div>
<div class="scroll"><table id="ft"></table></div>
</section>
<h2>Pages</h2>
<div class="hero" id="hero"></div>
<div class="controls">
  <input id="q" type="search" placeholder="Filter by route or file" aria-label="Filter by route or file">
  <select id="area" aria-label="Area"></select>
  <select id="status" aria-label="Test status"><option value="">Any test status</option><option value="none">Not covered</option><option value="weak">Unit or component tests only</option><option value="browser">Has a browser test</option></select>
  <select id="access" aria-label="Access"><option value="">Any access</option><option value="public">Public</option><option value="signed">Signed in</option><option value="param">Needs an id</option></select>
</div>
<div class="scroll"><table id="t"></table></div>
<script>
const D=/*DATA*/;
const $=id=>document.getElementById(id);
const esc=s=>String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const P=D.pages;
const status=p=>p.tests.length===0?'none':p.tests.some(t=>t.layer==='browser')?'browser':'weak';
const access=p=>p.signedIn?'signed':p.parameters?'param':'public';
const n=(f)=>P.filter(f).length;
$('meta').textContent=`Generated ${D.generated}. Worked out from the code: pages from @page directives, clicks from the links in the markup, tests from the test sources.`;
const card=(v,cls,l,s)=>`<div class="big"><div class="n ${cls}">${v}</div><div class="l">${l}</div><div class="s">${s}</div></div>`;
$('hero').innerHTML=
  card(P.length,'','Pages','routable pages in the site')+
  card(n(p=>status(p)!=='none'),n(p=>status(p)==='none')?'':'good','Mentioned by a test',`${n(p=>status(p)==='none')} not covered`)+
  card(n(p=>status(p)==='browser'),'','Have a browser test','a real browser drives them')+
  card(n(p=>p.smoke==='passed'),n(p=>p.smoke==='failed')?'bad':'good','Open cleanly in a real browser',`signed in as the QA admin; ${n(p=>p.smoke==='failed')} fail, ${n(p=>p.smoke==='skipped')} need a record the run cannot make, ${n(p=>p.smoke==='not run')} not run`)+
  card(n(p=>p.crawled),'','Visited by the site crawler','public pages reachable by links')+
  card(n(p=>p.clicks==null),n(p=>p.clicks==null)?'warn':'good','No link leads to them','reached by redirect, code or QR');
const areas=[...new Set(P.map(p=>p.area))].sort();
$('area').innerHTML='<option value="">All areas</option>'+areas.map(a=>`<option>${esc(a)}</option>`).join('');
const smokeCell=p=>p.smoke==='passed'?`<span class="good">opens</span> <span class="dim">${(p.smokeMilliseconds/1000).toFixed(1)} s</span>`:p.smoke==='failed'?`<span class="bad">fails</span><div class="dim">${esc((p.smokeProblems||[]).join('; ')).slice(0,160)}</div>`:p.smoke==='skipped'?`<span class="warn">needs a record</span>`:'<span class="dim">not run</span>';
let sortKey='route',sortDir=1;
const keyOf={route:p=>p.route,area:p=>p.area,clicks:p=>p.clicks==null?99:p.clicks,tests:p=>p.tests.length,crawled:p=>p.crawled?1:0};
function render(){
  const q=$('q').value.toLowerCase(),a=$('area').value,s=$('status').value,ac=$('access').value;
  const rows=P.filter(p=>(!q||p.route.toLowerCase().includes(q)||p.file.toLowerCase().includes(q))&&(!a||p.area===a)&&(!s||status(p)===s)&&(!ac||access(p)===ac))
    .sort((x,y)=>{const kx=keyOf[sortKey](x),ky=keyOf[sortKey](y);return (kx>ky?1:kx<ky?-1:0)*sortDir;});
  const head=[['route','Page'],['area','Area'],['clicks','Clicks from home'],['','Access'],['tests','Tests'],['crawled','Crawled'],['','Opens (browser)']];
  $('t').innerHTML='<tr>'+head.map(([k,l])=>`<th data-k="${k}" class="${k==='clicks'||k==='tests'?'num':''}">${l}</th>`).join('')+'</tr>'+rows.map(p=>{
    const layers={};p.tests.forEach(t=>layers[t.layer]=(layers[t.layer]||0)+1);
    const tags=Object.entries(layers).map(([l,c])=>`<span class="tag ${l}">${c} ${l}</span>`).join('')||'<span class="bad">not covered</span>';
    const list=p.tests.length?`<details><summary>${tags}</summary><ul>${p.tests.slice(0,25).map(t=>`<li class="mono">${esc(t.file.split('/').pop())} :: ${esc(t.method)}</li>`).join('')}${p.tests.length>25?`<li>and ${p.tests.length-25} more</li>`:''}</ul></details>`:tags;
    const clicks=p.clicks==null?'<span class="warn">no link</span>':p.clicks===0?'0 (home)':`${p.clicks}${p.via&&p.via!=='/'?` <span class="dim">via ${esc(p.via)}</span>`:''}`;
    const acc={public:'Public',signed:'Signed in',param:'Needs an id'}[access(p)];
    return `<tr><td><span class="mono">${esc(p.route)}</span><div class="dim mono">${esc(p.file.replace(/^src\//,''))}</div></td><td>${esc(p.area)}</td><td class="num">${clicks}</td><td>${acc}</td><td class="num">${list}</td><td>${p.crawled?'<span class="good">yes</span>':'<span class="dim">no</span>'}</td><td>${smokeCell(p)}</td></tr>`;}).join('');
  $('t').querySelectorAll('th[data-k]').forEach(th=>th.onclick=()=>{const k=th.dataset.k;if(!keyOf[k])return;sortDir=sortKey===k?-sortDir:1;sortKey=k;render();});
}
['q','area','status','access'].forEach(id=>$(id).addEventListener('input',render));
render();

// ---- functions ----
const F=D.functions;
if(F&&F.length){
  $('fn-section').hidden=false;
  const fstatus=f=>f.status;
  const nf=g=>F.filter(g).length;
  const people=F.filter(f=>f.who!=='System');
  const stepsTotal=people.reduce((a,f)=>a+f.clicks,0);
  $('fn-hero').innerHTML=
    card(F.length,'','Functions',`${people.length} a person does, ${F.length-people.length} that happen on their own`)+
    card(nf(f=>f.status==='none'),nf(f=>f.status==='none')?'bad':'good','Not covered by any test','no test class named for them exists')+
    card(nf(f=>f.status==='browser'),'','Have a browser test','a real browser drives part of them')+
    card(nf(f=>f.walked),'','Walked in a real browser','from start to finish by a Playwright test')+
    card(nf(f=>f.smoke==='opens cleanly'),nf(f=>f.smoke==='fails')?'bad':'good','Pages open cleanly',`${nf(f=>f.smoke==='partly'||f.smoke==='needs a record')} only partly (a page needs a record), ${nf(f=>f.smoke==='fails')} fail`)+
    card((stepsTotal/Math.max(1,people.length)).toFixed(1),'','Clicks on average',`across the ${people.length} functions a person does`);
  const warn=[];
  if(D.unassignedPages&&D.unassignedPages.length) warn.push(`<div class="warn"><b>${D.unassignedPages.length} page(s) belong to no function:</b> ${D.unassignedPages.map(r=>'<span class="mono">'+esc(r)+'</span>').join(', ')}</div>`);
  if(D.unknownRoutes&&D.unknownRoutes.length) warn.push(`<div class="bad"><b>Routes that are not pages:</b> ${D.unknownRoutes.map(u=>'<span class="mono">'+esc(u.function)+': '+esc(u.route)+'</span>').join(', ')}</div>`);
  const missing=F.filter(f=>f.missing.length);
  if(missing.length) warn.push(`<div class="bad"><b>Tests named in the list that do not exist:</b> ${missing.map(f=>esc(f.id)+' ('+f.missing.map(esc).join(', ')+')').join('; ')}</div>`);
  const um=D.unmappedTests||[];
  if(um.length) warn.push(`<details><summary class="warn">${um.length} test classes (${um.reduce((a,t)=>a+t.count,0)} tests) are named by no function - missing from the list, or testing something it does not mention</summary><ul>${um.map(t=>`<li class="mono">${esc(t.class)} <span class="dim">(${t.count}, ${esc(t.project.replace('TriviaNight.',''))})</span></li>`).join('')}</ul></details>`);
  $('fn-warn').innerHTML=warn.join('')||'<div class="good">Every page belongs to a function and every test named exists.</div>';
  const fa=[...new Set(F.map(f=>f.area))],fw=[...new Set(F.map(f=>f.who))];
  $('farea').innerHTML='<option value="">All areas</option>'+fa.map(a=>`<option>${esc(a)}</option>`).join('');
  $('fwho').innerHTML='<option value="">Anyone or any role</option>'+fw.map(a=>`<option>${esc(a)}</option>`).join('');
  const crawlCls=c=>c==='crawled'?'good':c==='not a page'?'dim':'warn';
  function renderF(){
    const q=$('fq').value.toLowerCase(),a=$('farea').value,w=$('fwho').value,s=$('fstatus').value;
    const rows=F.filter(f=>(!q||(f.name+' '+f.id+' '+f.routes.join(' ')).toLowerCase().includes(q))&&(!a||f.area===a)&&(!w||f.who===w)&&(!s||f.status===s));
    $('ft').innerHTML='<tr><th>Function</th><th class="num">Clicks</th><th>Pages</th><th class="num">Tests</th><th>Automatic checks</th></tr>'+rows.map(f=>{
      const steps=f.steps.length?`<details><summary>${f.clicks} click${f.clicks===1?'':'s'}${f.entries?` + ${f.entries} other`:''}</summary><ol>${f.steps.map(x=>`<li>${esc(x)}</li>`).join('')}</ol></details>`:'<span class="dim">automatic</span>';
      const layers={};f.tests.forEach(t=>layers[t.layer]=(layers[t.layer]||0)+t.count);
      const tags=Object.entries(layers).map(([l,c])=>`<span class="tag ${l}">${c} ${l}</span>`).join('')||'<span class="bad">not covered</span>';
      const list=`<details><summary>${tags}${f.missing.length?` <span class="bad">${f.missing.length} missing</span>`:''}</summary><ul>${f.tests.map(t=>`<li class="mono">${esc(t.name)} <span class="dim">(${t.count} ${esc(t.layer)}, ${esc(t.project.replace('TriviaNight.',''))})</span></li>`).join('')}${f.missing.map(m=>`<li class="mono bad">${esc(m)} - no such test</li>`).join('')}</ul></details>`;
      const pagesCell=f.routes.length?f.routes.map(r=>`<div class="mono">${esc(r)}</div>`).join(''):'<span class="dim">no page</span>';
      const depth=f.navigationDepth==null?'':` <span class="dim">(page is ${f.navigationDepth} click${f.navigationDepth===1?'':'s'} from home)</span>`;
      return `<tr><td><b>${esc(f.name)}</b><div class="dim">${esc(f.area)} · ${esc(f.who)}${f.walked?' · <span class="good">walked in a browser</span>':''}</div></td><td class="num">${steps}${depth}</td><td>${pagesCell}</td><td class="num">${f.testCount?list:tags}${f.testCount?'':''}</td><td><div><span class="${crawlCls(f.crawl)}">crawl: ${esc(f.crawl)}</span></div><div><span class="${f.smoke==='opens cleanly'?'good':f.smoke==='fails'?'bad':'warn'}">browser: ${esc(f.smoke)}</span></div></td></tr>`;}).join('');
  }
  ['fq','farea','fwho','fstatus'].forEach(id=>$(id).addEventListener('input',renderF));
  renderF();
}
</script></main></body></html>
""";
}
