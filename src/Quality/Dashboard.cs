/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Text.Json;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// One commit on the dashboard: the numbers as they stood the last time a run measured them at that commit. <see cref="Smells"/> leaves out the rules added after the first recording
/// (<see cref="Rules.Newer"/>) so that adding a check never makes the line climb; <see cref="SmellsAll"/> counts everything.
/// </summary>
internal sealed record DashboardPoint(string Commit, string At, int Smells, int SmellsAll, double Pressure, int? Analyzers, int? Tests, int? Failed, double? Coverage);

/// <summary>One push's check run: when it was, how long its slowest stage took (the stages ran side by side, so that is what the push waited for) and whether every stage passed.</summary>
internal sealed record GateRun(string At, double SlowestSeconds, bool Passed);

/// <summary>
/// The dashboard: the quality timeline (<c>quality/history.json</c>, one value set per commit) and the push timings (<c>reports/check-timings.jsonl</c>) drawn as trend lines, with where each number started, where it
/// is now and how it moved since the previous commit. One self-contained page at a fixed name (<c>reports/quality-dashboard.html</c>), rewritten by every run so it can stay open in a tab or be bookmarked.
/// </summary>
internal static class Dashboard
{
    public const string FileName = "quality-dashboard.html";

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// One point per commit, oldest first. A commit measured several times keeps its last smell counts and its fullest test numbers: the push check runs the unit tests only and the background run all of them, so
    /// the two alternate within a commit. A commit where only the small set ran (far fewer tests than the recent full runs) shows no test numbers rather than a dip that never happened.
    /// </summary>
    public static List<DashboardPoint> Points(History history)
    {
        var points = new List<DashboardPoint>();
        foreach (var snapshot in history.Snapshots.Where(s => s.Smells is not null))
        {
            var (smells, pressure) = snapshot.Smells!.Without(Rules.Newer);
            var tests = points.Count > 0 && points[^1].Commit == snapshot.Commit ? FullerTests(points[^1], snapshot.Tests) : snapshot.Tests;
            var point = new DashboardPoint(snapshot.Commit, snapshot.At, smells, snapshot.Smells.Total, pressure, snapshot.Analyzers?.Total, tests?.Total, tests?.Failed, tests?.Coverage);
            if (points.Count > 0 && points[^1].Commit == point.Commit)
            {
                points[^1] = point;
            }
            else
            {
                points.Add(point);
            }
        }

        return HideSmallRuns(points);
    }

    // The fuller run's test numbers, with the lowest coverage either run recorded: the small set covers more of what it runs, and a headline must not overstate.
    private static TestCounts? FullerTests(DashboardPoint earlier, TestCounts? later)
    {
        TestCounts? before = earlier.Tests is { } total ? new TestCounts(total, earlier.Failed ?? 0, earlier.Coverage) : null;
        var fuller = later is null || (before is not null && before.Total > later.Total) ? before : later;
        return fuller is null ? null : fuller with { Coverage = LowerOf(before?.Coverage, later?.Coverage) };
    }

    private static double? LowerOf(double? a, double? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    /// <summary>
    /// The coverage to put in a headline: the lowest of this run's and anything recorded at the last few commits, so a quick unit-only run never lifts the number above what the full run measured.
    /// </summary>
    public static double? ConservativeCoverage(History history, double? current)
    {
        var recent = history.Snapshots.Where(s => s.Tests?.Coverage is not null).Select(s => (s.Commit, Coverage: s.Tests!.Coverage!.Value)).ToList();
        var lastCommits = recent.Select(r => r.Commit).Distinct().TakeLast(3).ToHashSet();
        var lowest = recent.Where(r => lastCommits.Contains(r.Commit)).Select(r => (double?)r.Coverage).DefaultIfEmpty(null).Min();
        return LowerOf(current, lowest);
    }

    private const int RecentWindow = 4;
    private const double SmallRunShare = 0.8;

    private static List<DashboardPoint> HideSmallRuns(List<DashboardPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var recentFull = points.Skip(Math.Max(0, i - RecentWindow)).Take(Math.Min(i, RecentWindow)).Max(p => p.Tests) ?? 0;
            if (points[i].Tests is { } total && total < recentFull * SmallRunShare)
            {
                points[i] = points[i] with { Tests = null, Failed = null, Coverage = null };
            }
        }

        return points;
    }

    /// <summary>The pushes in the timing log (one JSON line per stage, all stages of a push sharing one timestamp), oldest first.</summary>
    public static List<GateRun> GateRuns(IEnumerable<string> lines)
    {
        var byPush = new Dictionary<string, List<(double Seconds, bool Passed)>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (TryParseStage(line, out var at, out var seconds, out var passed))
            {
                if (!byPush.TryGetValue(at, out var stages))
                {
                    byPush[at] = stages = [];
                }

                stages.Add((seconds, passed));
            }
        }

        return byPush.Select(p => new GateRun(p.Key, p.Value.Max(s => s.Seconds), p.Value.All(s => s.Passed))).OrderBy(r => r.At, StringComparer.Ordinal).ToList();
    }

    private static bool TryParseStage(string line, out string at, out double seconds, out bool passed)
    {
        at = string.Empty;
        seconds = 0;
        passed = false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            at = root.GetProperty("at").GetString() ?? string.Empty;
            seconds = root.GetProperty("seconds").GetDouble();
            passed = root.GetProperty("passed").GetBoolean();
            return at.Length > 0;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;   // a half-written or foreign line in an append-only log is skipped, not fatal
        }
    }

    public static string Render(string title, string commit, History history, IReadOnlyList<GateRun> gateRuns, IReadOnlyList<TargetStatus>? release = null, string? consoleUrl = null)
    {
        var first = history.Snapshots.FirstOrDefault(s => s.Smells is not null)?.Smells;
        var last = history.Snapshots.LastOrDefault(s => s.Smells is not null)?.Smells;
        var data = new
        {
            generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            commit,
            points = Points(history),
            gate = gateRuns.TakeLast(60).ToList(),
            newerRules = Rules.Newer,
            release,
            consoleUrl,
            firstRules = first?.ByRule,
            lastRules = last?.ByRule,
        };
        var json = JsonSerializer.Serialize(data, CamelCase).Replace("<", "\\u003c", StringComparison.Ordinal);
        return Template.Replace("/*DATA*/", json, StringComparison.Ordinal).Replace("/*TITLE*/", System.Net.WebUtility.HtmlEncode(title), StringComparison.Ordinal);
    }

    public static async Task<string> WriteAsync(string root, History history, ToolConfig config)
    {
        var commit = (await Shell.RunAsync("git", "rev-parse --short HEAD", root, 20)).Output.Trim();
        var timings = Path.Combine(Repo.OutputRoot(root), "reports", "check-timings.jsonl");
        var runs = File.Exists(timings) ? GateRuns(File.ReadLines(timings)) : [];
        var path = Path.Combine(Repo.OutputRoot(root), "reports", FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var release = await ReleaseStatusAsync(root, config);
        await File.WriteAllTextAsync(path, Render(config.Title(root), commit, history, runs, release, config.Release?.ConsoleUrl));
        return path;
    }

    // What is running where, asked of the live sites each time the dashboard is written (a site that does not answer shows as unknown, never as an error).
    private static async Task<List<TargetStatus>?> ReleaseStatusAsync(string root, ToolConfig config)
    {
        if (config.Release is not { Targets.Length: > 0 } release)
        {
            return null;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        return await ReleasePanel.BuildAsync(release, url => FetchAsync(http, url), new RepoGit(root));
    }

    private static async Task<string?> FetchAsync(HttpClient http, string url)
    {
        try
        {
            return await http.GetStringAsync(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;   // the panel says the build could not be read
        }
    }

    private const string Template = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>/*TITLE*/ - code quality dashboard</title>
<style>
:root{--bg:#f6f5f1;--card:#fff;--ink:#1d2030;--dim:#5d6378;--line:#e3e1d8;--accent:#b9791f;--bad:#b3261e;--ok:#1b7a43;--chip:#efece3;--grid:#e8e5db}
@media (prefers-color-scheme:dark){:root{--bg:#0f1220;--card:#181c30;--ink:#eceefb;--dim:#9aa1c4;--line:#2a2f4a;--accent:#e0a64b;--bad:#ff8a80;--ok:#6fd49a;--chip:#232845;--grid:#262b45}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Segoe UI",system-ui,sans-serif}
main{max-width:1180px;margin:0 auto;padding:20px 16px 60px}
h1{font-size:1.6rem;margin:.2rem 0}h2{font-size:1.1rem;margin:0 0 .4rem}
.dim{color:var(--dim)}a{color:var(--accent)}
.hero{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:14px;margin:16px 0}
.big{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px 20px}
.big .n{font-size:3.2rem;font-weight:700;line-height:1;font-variant-numeric:tabular-nums}.big .l{font-weight:600;margin-top:.4rem}.big .s{color:var(--dim);margin-top:.25rem;font-size:.88rem}
.good{color:var(--ok)}.bad{color:var(--bad)}
.charts{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr));gap:14px}
.panel{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 16px;min-width:0}
svg{width:100%;height:auto;display:block}svg text{fill:var(--dim);font:11px "Segoe UI",system-ui,sans-serif}
.legend{display:flex;gap:14px;flex-wrap:wrap;font-size:.82rem;color:var(--dim);margin-top:4px}.legend i{display:inline-block;width:14px;height:3px;border-radius:2px;margin-right:5px;vertical-align:middle}
table{width:100%;border-collapse:collapse;font-size:.9rem}th,td{padding:5px 10px;text-align:left;border-bottom:1px solid var(--line)}th{background:var(--chip)}td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}tr:last-child td{border-bottom:0}
.scroll{overflow-x:auto}
.btn{display:inline-block;padding:7px 16px;border-radius:8px;background:var(--accent);color:#fff;text-decoration:none;font-weight:600}
ul{margin:4px 0 4px 18px;padding:0}.mono{font:13px Consolas,monospace}
</style></head><body><main>
<h1>/*TITLE*/ - code quality dashboard</h1>
<div class="dim" id="meta"></div>
<div class="hero" id="hero"></div>
<div id="release"></div>
<div class="charts" id="charts"></div>
<div class="charts" style="margin-top:14px">
  <div class="panel"><h2>Code smells by rule</h2><div class="scroll"><table id="rules"></table></div></div>
  <div class="panel"><h2>Recent commits</h2><div class="scroll"><table id="recent"></table></div></div>
</div>
<script>
const D=/*DATA*/;
const $=id=>document.getElementById(id);
const esc=s=>String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const P=D.points, G=D.gate;
const at=P.length?P[P.length-1]:null, start=P.length?P[0]:null, prev=P.length>1?P[P.length-2]:null;
$('meta').innerHTML=`Generated ${esc(D.generated)} at commit ${esc(D.commit)}. ${P.length} commits measured since ${start?esc(start.at.slice(0,10)):'-'}. <a href="quality-latest.html">Open the full report</a>.`;

function delta(now,before,goodDown){
  if(before==null||now==null) return '';
  const d=now-before; if(d===0) return 'no change since the last commit';
  const word=d<0?`down ${Math.abs(d)}`:`up ${d}`;
  return `<span class="${(d<0)===goodDown?'good':'bad'}">${word}</span> since the last commit`;
}
function card(n,cls,label,sub){return `<div class="big"><div class="n ${cls}">${n}</div><div class="l">${label}</div><div class="s">${sub}</div></div>`;}
let hero='';
if(at){
  const better=at.smells<=start.smells;
  hero+=card(at.smells,better?'good':'bad','Code smells',`from ${start.smells} at the first recording. ${delta(at.smells,prev?.smells,true)}`);
}
if(at&&at.tests!=null){
  const cov=at.coverage!=null?`${(at.coverage*100).toFixed(1)}% line coverage`:'coverage not measured';
  hero+=card(at.tests.toLocaleString(),at.failed?'bad':'',at.failed?`Tests, ${at.failed} failing`:'Tests, all passing',`${cov}. ${delta(at.tests,prev?.tests,false).replace('good','good')}`);
}
if(at&&at.analyzers!=null) hero+=card(at.analyzers,at.analyzers===0?'good':'',`Analyzer findings`,`${delta(at.analyzers,prev?.analyzers,true)}`);
if(G.length){const g=G[G.length-1];hero+=card(Math.round(g.slowestSeconds)+' s',g.passed?'':'bad','Last push check',`slowest stage; ${g.passed?'every stage passed':'a stage failed'}. Median of the last ${Math.min(G.length,20)}: ${Math.round(median(G.slice(-20).map(x=>x.slowestSeconds)))} s`);}
$('hero').innerHTML=hero||'<div class="panel">No measurements yet. Run the quality check once.</div>';
function median(a){const s=[...a].sort((x,y)=>x-y);const m=s.length>>1;return s.length%2?s[m]:(s[m-1]+s[m])/2;}

// A line chart drawn to scale: one x slot per measurement, y from zero to a rounded-up maximum, the last value labelled.
function chart(title,labels,series,fmt){
  const W=560,H=210,L=44,R=46,T=14,B=26;
  const vals=series.flatMap(s=>s.values).filter(v=>v!=null);
  if(vals.length<2) return `<div class="panel"><h2>${title}</h2><div class="dim">Not enough measurements yet.</div></div>`;
  const max=niceMax(Math.max(...vals)), n=labels.length;
  const x=i=>L+(n===1?0:i*(W-L-R)/(n-1)), y=v=>T+(H-T-B)*(1-v/max);
  let g='';
  for(let k=0;k<=4;k++){const v=max*k/4;g+=`<line x1="${L}" x2="${W-R}" y1="${y(v)}" y2="${y(v)}" stroke="var(--grid)"/><text x="${L-6}" y="${y(v)+4}" text-anchor="end">${fmt(v)}</text>`;}
  g+=`<text x="${L}" y="${H-6}">${esc(labels[0])}</text><text x="${W-R}" y="${H-6}" text-anchor="end">${esc(labels[n-1])}</text>`;
  for(const s of series){
    const pts=s.values.map((v,i)=>v==null?null:[x(i),y(v)]).filter(Boolean);
    if(!pts.length) continue;
    const d=pts.map((p,i)=>(i?'L':'M')+p[0].toFixed(1)+' '+p[1].toFixed(1)).join(' ');
    if(s.area) g+=`<path d="${d} L${pts[pts.length-1][0].toFixed(1)} ${y(0)} L${pts[0][0].toFixed(1)} ${y(0)} Z" fill="${s.color}" opacity=".12"/>`;
    g+=`<path d="${d}" fill="none" stroke="${s.color}" stroke-width="${s.thin?1.5:2.4}" ${s.dash?'stroke-dasharray="5 4"':''} stroke-linejoin="round"/>`;
    const e=pts[pts.length-1], ev=s.values.filter(v=>v!=null).slice(-1)[0];
    g+=`<circle cx="${e[0]}" cy="${e[1]}" r="3.6" fill="${s.color}"/><text x="${e[0]+7}" y="${e[1]+4}" style="fill:${s.color};font-weight:600">${fmt(ev)}</text>`;
    s.values.forEach((v,i)=>{if(v!=null) g+=`<circle cx="${x(i)}" cy="${y(v)}" r="7" fill="transparent"><title>${esc(labels[i])}: ${fmt(v)}</title></circle>`;});
  }
  const legend=series.map(s=>`<span><i style="background:${s.color}"></i>${esc(s.name)}</span>`).join('');
  return `<div class="panel"><h2>${title}</h2><svg viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(title)}">${g}</svg><div class="legend">${legend}</div></div>`;
}
function niceMax(v){if(v<=0) return 1;const p=Math.pow(10,Math.floor(Math.log10(v)));for(const m of [1,1.5,2,3,5,10]) if(v<=m*p) return m*p;return 10*p;}
const int=v=>Math.round(v).toLocaleString();
const lab=P.map(p=>`${p.commit} ${p.at.slice(5,10)}`);
let charts='';
charts+=chart('Code smells per commit',lab,[{name:'Smells (the original rules)',values:P.map(p=>p.smells),color:'var(--accent)',area:true},{name:'All rules, including newer checks',values:P.map(p=>p.smellsAll),color:'var(--dim)',dash:true,thin:true}],int);
charts+=chart('Refactoring pressure',lab,[{name:'How far over its limit each smell is, summed',values:P.map(p=>p.pressure),color:'var(--bad)',area:true}],v=>v.toFixed(0));
charts+=chart('Tests',lab,[{name:'Tests',values:P.map(p=>p.tests),color:'var(--ok)',area:true}],int);
charts+=chart('Line coverage',lab,[{name:'Share of lines run by the tests (Razor not included)',values:P.map(p=>p.coverage==null?null:p.coverage*100),color:'var(--ok)'}],v=>v.toFixed(0)+'%');
charts+=chart('Analyzer findings',lab,[{name:'.NET analyzer findings',values:P.map(p=>p.analyzers),color:'var(--bad)'}],int);
charts+=chart('Push check: slowest stage',G.map(g=>g.at.slice(5,16).replace('T',' ')),[{name:'Seconds the push waited',values:G.map(g=>g.slowestSeconds),color:'var(--accent)'}],v=>Math.round(v)+' s');
$('charts').innerHTML=charts;

// Release: what each environment is running, how far behind it is, and a button into the deploy console (which asks before it does anything).
if(D.release&&D.release.length){
  const cards=D.release.map(t=>{
    const behind=t.problem?`<div class="bad">${esc(t.problem)}</div>`
      :t.commitsAhead===0?`<div class="good">Up to date with ${esc(t.source)}.</div>`
      :`<div>${t.commitsAhead} commit${t.commitsAhead===1?'':'s'} and ${t.filesChanged} file${t.filesChanged===1?'':'s'} newer than what is running, from ${esc(t.source)}.</div>`;
    const warn=t.warn&&t.sensitive.length?`<div class="bad"><b>Changes who can do what, or payments</b> (${t.sensitive.length} file${t.sensitive.length===1?'':'s'}):<ul>${t.sensitive.slice(0,12).map(f=>'<li class="mono">'+esc(f)+'</li>').join('')}${t.sensitive.length>12?'<li>and '+(t.sensitive.length-12)+' more</li>':''}</ul></div>`:'';
    return `<div class="panel"><h2>${esc(t.name)}</h2><div class="dim">Running build ${esc(t.build||'unknown')}</div>${behind}${warn}${D.consoleUrl?`<p><a class="btn" href="${esc(D.consoleUrl)}" target="_blank" rel="noopener">${esc(t.button)}</a></p>`:''}</div>`;}).join('');
  $('release').innerHTML=`<h2 style="margin-top:18px">Release</h2><div class="charts">${cards}</div><div class="dim" style="margin:6px 0 14px">The buttons open the deploy console (${esc(D.consoleUrl||'not set')}), which asks you to confirm before anything is deployed. Start it with: dotnet run --project tools/Deploy -- console</div>`;
}

// Where the smells are: now against the first recording, with the rules added later marked.
const fr=D.firstRules||{}, lr=D.lastRules||{};
const names=[...new Set([...Object.keys(fr),...Object.keys(lr)])].sort((a,b)=>(lr[b]||0)-(lr[a]||0));
$('rules').innerHTML='<tr><th>Rule</th><th class="num">At the start</th><th class="num">Now</th></tr>'+names.map(r=>{
  const newer=D.newerRules.includes(r), a=fr[r], b=lr[r]||0;
  const cls=a==null?'':(b<a?'good':b>a?'bad':'');
  return `<tr><td>${esc(r)}${newer?' <span class="dim">(added later)</span>':''}</td><td class="num">${a==null?'-':a}</td><td class="num ${cls}">${b}</td></tr>`;}).join('');
$('recent').innerHTML='<tr><th>Commit</th><th>When</th><th class="num">Smells</th><th class="num">Tests</th><th class="num">Coverage</th></tr>'+[...P].reverse().slice(0,15).map(p=>
  `<tr><td class="mono">${esc(p.commit)}</td><td>${esc(p.at)}</td><td class="num">${p.smells}</td><td class="num">${p.tests??'-'}</td><td class="num">${p.coverage==null?'-':(p.coverage*100).toFixed(1)+'%'}</td></tr>`).join('');
</script></main></body></html>
""";
}
