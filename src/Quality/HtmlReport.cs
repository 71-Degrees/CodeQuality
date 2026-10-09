/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// The latest result of each check, with when it ran. Kept in <c>reports/quality-state.json</c> so a quick run of one check does not wipe the others from the report: the HTML always shows the most
/// recent result of every check that has ever been run, each stamped with its time.
/// </summary>
internal sealed class Collected
{
    private static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };

    public IReadOnlyList<Finding> Smells { get; set; } = Array.Empty<Finding>();

    public IReadOnlyList<Finding> Analyzers { get; set; } = Array.Empty<Finding>();

    public CrawlResult? Crawl { get; set; }

    public string? CrawlUrl { get; set; }

    public string? Scripts { get; set; }

    public string? SmellsAt { get; set; }

    public string? AnalyzersAt { get; set; }

    public string? CrawlAt { get; set; }

    public string? ScriptsAt { get; set; }

    /// <summary>Which checks ran in this run (not saved): the timeline records numbers only for those.</summary>
    [JsonIgnore]
    public HashSet<string> RanThisRun { get; } = new();

    public static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm");

    private static string StatePath(string root) => Path.Combine(root, "reports", "quality-state.json");

    public static Collected Load(string root)
    {
        try
        {
            return File.Exists(StatePath(root)) ? JsonSerializer.Deserialize<Collected>(File.ReadAllText(StatePath(root))) ?? new Collected() : new Collected();
        }
        catch (JsonException)
        {
            return new Collected();   // an unreadable state file just means starting the report afresh
        }
    }

    public void Save(string root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath(root))!);
        File.WriteAllText(StatePath(root), JsonSerializer.Serialize(this, StateJson));
    }
}

/// <summary>
/// A single self-contained HTML page (no internet needed, light and dark) with everything a run found: where the code is big, which rules fire most and what each one means, a filterable
/// list of every finding with its status against the baseline, the .NET analyzer results, the site crawl and the browser-script checks - and the commands to run it all again.
/// </summary>
internal static class HtmlReport
{
    public static async Task<string> WriteAsync(string root, Collected collected, Baseline baseline, History history, ToolConfig config, bool includeTests)
    {
        var commit = (await Shell.RunAsync("git", "rev-parse --short HEAD", root, 20)).Output.Trim();
        var data = new
        {
            generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            commit,
            root,
            howTo = new { tool = config.ToolCommand, extra = config.ExtraCommands },
            history = history.Snapshots,
            findings = collected.Smells.Concat(collected.Analyzers).Select(f => Row(f, baseline)).ToList(),
            files = Smells.CountLines(root, includeTests, config).Select(f => new { file = f.File, lines = f.Lines }).ToList(),
            rules = collected.Smells.Concat(collected.Analyzers).Select(f => f.Rule).Distinct().ToDictionary(r => r, r => Describe(r)),
            crawl = collected.Crawl is null ? null : new
            {
                url = collected.CrawlUrl,
                pages = collected.Crawl.Pages,
                issues = collected.Crawl.Issues.Select(i => new { severity = i.Severity.ToString(), page = i.Page, text = i.Text }).ToList(),
            },
            scripts = collected.Scripts,
            ranAt = new { smells = collected.SmellsAt, analyzers = collected.AnalyzersAt, crawl = collected.CrawlAt, scripts = collected.ScriptsAt },
        };
        var json = JsonSerializer.Serialize(data).Replace("<", "\\u003c", StringComparison.Ordinal);
        var path = Path.Combine(root, "reports", $"quality-{DateTime.Now:yyyy-MM-dd_HHmm}.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var page = Template.Replace("/*DATA*/", json, StringComparison.Ordinal).Replace("/*TITLE*/", System.Net.WebUtility.HtmlEncode(config.Title(root)), StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, page);
        File.Copy(path, Path.Combine(root, "reports", "quality-latest.html"), overwrite: true);
        return path;
    }

    private static object Row(Finding f, Baseline baseline)
    {
        var status = baseline.Entries.TryGetValue(f.Key, out var was) ? (f.Value > was ? "worse" : "accepted") : baseline.Entries.Count == 0 ? "unbaselined" : "new";
        var weight = Weights.Of(f.Rule, f.Value);
        return new { rule = f.Rule, file = f.File, member = f.Member, line = f.Line, value = f.Value, message = f.Message, status, weight = Math.Round(weight, 2), automation = Rules.For(f.Rule).Automation.ToString() };
    }

    private static object Describe(string rule)
    {
        var info = Rules.For(rule);
        return new { title = info.Title, why = info.Why, fix = info.Fix, automation = info.Automation.ToString() };
    }

    /// <summary>Opens the report in Chrome (the default browser if Chrome is not found).</summary>
    public static void Open(string path)
    {
        var chrome = FindChrome();
        var url = new Uri(path).AbsoluteUri;
        Process.Start(chrome is null ? new ProcessStartInfo(url) { UseShellExecute = true } : new ProcessStartInfo(chrome, $"--new-tab \"{url}\"") { UseShellExecute = false });
    }

    private static string? FindChrome()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key?.GetValue(null) is string found && File.Exists(found))
            {
                return found;
            }
        }

        return null;
    }

    private const string Template = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>/*TITLE*/ - code quality report</title>
<style>
:root{--bg:#f6f5f1;--card:#fff;--ink:#1d2030;--dim:#5d6378;--line:#e3e1d8;--accent:#b9791f;--bad:#b3261e;--warn:#a65f00;--ok:#1b7a43;--chip:#efece3}
@media (prefers-color-scheme:dark){:root{--bg:#0f1220;--card:#181c30;--ink:#eceefb;--dim:#9aa1c4;--line:#2a2f4a;--accent:#e0a64b;--bad:#ff8a80;--warn:#ffb74d;--ok:#6fd49a;--chip:#232845}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Segoe UI",system-ui,sans-serif}
main{max-width:1180px;margin:0 auto;padding:20px 16px 60px}
h1{font-size:1.6rem;margin:.2rem 0}h2{font-size:1.2rem;margin:2rem 0 .6rem;border-bottom:2px solid var(--accent);padding-bottom:.25rem}
.dim{color:var(--dim)}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:10px;margin:14px 0}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}.card b{display:block;font-size:1.7rem;line-height:1.1}
.panel{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 16px;margin:10px 0}
table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:hidden;font-size:.9rem}
th,td{padding:6px 10px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top}th{background:var(--chip);cursor:pointer;white-space:nowrap;user-select:none}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}tr:last-child td{border-bottom:0}
.tag{display:inline-block;padding:1px 8px;border-radius:99px;font-size:.75rem;background:var(--chip);white-space:nowrap}
.Mechanical{color:var(--ok)}.Assisted{color:var(--warn)}.Manual{color:var(--bad)}.new,.worse{color:var(--bad);font-weight:600}.accepted{color:var(--dim)}
.bar{height:8px;border-radius:4px;background:var(--accent);min-width:2px}code,.mono{font:13px Consolas,monospace}
.controls{display:flex;flex-wrap:wrap;gap:8px;margin:8px 0}input,select{font:inherit;padding:5px 8px;border-radius:6px;border:1px solid var(--line);background:var(--card);color:var(--ink)}
details{margin:6px 0}summary{cursor:pointer;font-weight:600}.scroll{overflow-x:auto}pre{background:var(--chip);padding:8px 12px;border-radius:8px;overflow-x:auto;margin:6px 0}
</style></head><body><main>
<h1>/*TITLE*/ - code quality report</h1>
<div class="dim" id="meta"></div>
<div class="cards" id="cards"></div>

<h2>Quality over time</h2>
<div id="trend"></div>

<h2>How to run this yourself</h2>
<div class="panel">Everything here is read-only and runs from the repository root (<code id="rootdir"></code>). Each run rewrites this page (<code>reports\quality-latest.html</code>).
<pre id="howto"></pre>
Full guide: the CodeQuality repository's README.</div>

<h2>Where the code is big</h2>
<div class="dim">Files ranked by how much refactoring pressure they carry (sum of finding weights: how far over its limit each one is). Click a heading to sort.</div>
<div class="scroll"><table id="hot"></table></div>

<h2>Rules that fired</h2>
<div class="dim"><span class="Mechanical">Mechanical</span> = a tool can fix it with no judgment, <span class="Assisted">Assisted</span> = a tool can do the typing once a person picks where, <span class="Manual">Manual</span> = a decision about what the code should do.</div>
<div id="rules"></div>

<h2>Biggest single problems</h2>
<div class="scroll"><table id="top"></table></div>

<h2>Every finding</h2>
<div class="controls"><input id="q" placeholder="filter by file, member or text" size="34"><select id="fr"></select><select id="fs"><option value="">any status</option><option>new</option><option>worse</option><option>accepted</option><option>unbaselined</option></select><select id="fa"><option value="">any automation</option><option>Mechanical</option><option>Assisted</option><option>Manual</option></select><span class="dim" id="count"></span></div>
<div class="scroll"><table id="all"></table></div>

<div id="extra"></div>
</main>
<script id="data" type="application/json">/*DATA*/</script>
<script>
const D = JSON.parse(document.getElementById('data').textContent);
const $ = id => document.getElementById(id);
const esc = s => String(s).replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const F = D.findings;

function sortable(table, rows, cols) {
  let key = null, dir = -1;
  const draw = () => {
    const sorted = key === null ? rows : [...rows].sort((a, b) => (a[key] > b[key] ? 1 : a[key] < b[key] ? -1 : 0) * dir);
    table.innerHTML = '<tr>' + cols.map(c => `<th class="${c.num ? 'num' : ''}" data-k="${c.k}">${c.h}</th>`).join('') + '</tr>' +
      sorted.map(r => '<tr>' + cols.map(c => `<td class="${c.num ? 'num' : ''}">${c.f ? c.f(r) : esc(r[c.k])}</td>`).join('') + '</tr>').join('');
    table.querySelectorAll('th').forEach(th => th.onclick = () => { const k = th.dataset.k; dir = key === k ? -dir : -1; key = k; draw(); });
  };
  draw();
}

// summary
const count = s => F.filter(f => f.status === s).length;
const smells = F.filter(f => !f.rule.startsWith('build/')), analyzers = F.filter(f => f.rule.startsWith('build/'));
const linesTotal = D.files.reduce((n, f) => n + f.lines, 0);
const when = t => t || 'never run';
$('rootdir').textContent = D.root;
const tool = D.howTo.tool;
$('howto').textContent = [
  tool + ' --open                      smells + .NET analyzers + script checks, then open this report (about a minute)',
  tool + ' smells --open               code smells only (seconds)',
  tool + ' crawl --open                walk the public site in headless Chromium (needs ~1 GB free memory)',
  tool + ' smells --update-baseline    accept today\'s findings (after fixing things, or on purpose); then commit quality/baseline.json',
  tool + ' history --backfill          rebuild the timeline from the git history of the baseline',
  ...D.howTo.extra].join('\n');
$('meta').innerHTML = `Generated ${esc(D.generated)} at commit ${esc(D.commit)}. ${D.files.length} source files, ${linesTotal.toLocaleString()} lines (C# and Razor).<br>Latest runs - smells: ${esc(when(D.ranAt.smells))}; analyzers: ${esc(when(D.ranAt.analyzers))}; scripts: ${esc(when(D.ranAt.scripts))}; site crawl: ${esc(when(D.ranAt.crawl))}.`;
const card = (n, t, cls) => `<div class="card"><b class="${cls || ''}">${n}</b>${t}</div>`;
$('cards').innerHTML = card(smells.length, 'code smells') + card(analyzers.length, '.NET analyzer findings') +
  card(count('new') + count('worse'), 'new or worse than baseline', count('new') + count('worse') ? 'new' : '') +
  card(F.filter(f => f.automation === 'Mechanical').length, 'could be fixed mechanically') +
  (D.crawl ? card(D.crawl.issues.filter(i => i.severity === 'Error').length, `site errors (${D.crawl.pages} pages crawled)`) : '');

// quality over time: where it started, last time, now
(function () {
  const H = D.history || [];
  if (H.length === 0) { $('trend').innerHTML = '<div class="panel dim">No timeline yet. Run <code>quality history --backfill</code> once (see "How to run this yourself"), then every run adds to it.</div>'; return; }
  const first = H[0], now = H[H.length - 1], prev = H.length > 1 ? H[H.length - 2] : null;
  const val = (s, part, key) => (s && s[part]) ? s[part][key] : null;
  const num = v => Math.round(v * 10) / 10;
  const fmt = v => v === null ? '<span class="dim">not measured</span>' : num(v).toLocaleString();
  const delta = (a, b) => (a === null || b === null) ? '' : (b === a ? '<span class="dim">no change</span>' : `<span class="${b < a ? 'Mechanical' : 'new'}">${b < a ? '' : '+'}${num(b - a)} (${b < a ? 'better' : 'worse'})</span>`);
  const label = s => s ? `${esc(s.At)}<br><span class="dim mono">${esc(s.Commit)}</span>` : '';
  const metrics = [['Code smells', 'Smells', 'Total'], ['Refactoring pressure', 'Smells', 'Pressure'], ['.NET analyzer findings', 'Analyzers', 'Total']];
  let html = `<div class="scroll"><table><tr><th>Measure</th><th class="num">Start<br>${label(first)}</th><th class="num">Before the last change<br>${label(prev)}</th><th class="num">Now<br>${label({ At: D.generated, Commit: D.commit })}</th><th>Change since start</th></tr>` +
    metrics.map(([name, part, key]) => { const a = val(first, part, key), p = val(prev, part, key), n = val(now, part, key); return `<tr><td>${name}</td><td class="num">${fmt(a)}</td><td class="num">${prev ? fmt(p) : ''}</td><td class="num">${fmt(n)}</td><td>${delta(a, n)}</td></tr>`; }).join('') + '</table></div>';
  html += '<div class="dim">Pressure adds up how far over its limit each smell is (a 150-line method weighs about 2.5; a 61-line one 1). The start is the earliest snapshot recorded (rebuilt from the git history of the baseline for the first day); every run that changes the numbers adds a point.</div>';

  const rules = [...new Set([...Object.keys((first.Smells || {}).ByRule || {}), ...Object.keys((now.Smells || {}).ByRule || {})])].sort();
  const at = (s, r) => ((s.Smells || {}).ByRule || {})[r] || 0;
  html += '<details><summary>By rule</summary><div class="scroll"><table><tr><th>Rule</th><th class="num">Start</th><th class="num">Now</th><th>Change</th></tr>' +
    rules.map(r => `<tr><td>${esc(r)}</td><td class="num">${at(first, r)}</td><td class="num">${at(now, r)}</td><td>${delta(at(first, r), at(now, r))}</td></tr>`).join('') + '</table></div></details>';

  const pts = H.filter(s => s.Smells).map(s => ({ at: s.At, v: s.Smells.Pressure }));
  if (pts.length > 1) {
    const W = 700, T = 150, max = Math.max(...pts.map(p => p.v)) || 1;
    const x = i => 20 + i * (W - 40) / (pts.length - 1), y = v => T - 24 - (v / max) * (T - 50);
    html += `<div class="panel"><b>Refactoring pressure over time</b> <span class="dim">(one point per recorded change; lower is better)</span>
      <svg viewBox="0 0 ${W} ${T}" role="img" aria-label="Refactoring pressure over time, from ${num(pts[0].v)} to ${num(pts[pts.length - 1].v)}" style="width:100%;max-width:${W}px">
      <polyline fill="none" stroke="var(--accent)" stroke-width="2" points="${pts.map((p, i) => x(i) + ',' + y(p.v)).join(' ')}"/>
      ${pts.map((p, i) => `<circle cx="${x(i)}" cy="${y(p.v)}" r="4" fill="var(--accent)"><title>${esc(p.at)}: ${num(p.v)}</title></circle>`).join('')}
      <text x="${x(0)}" y="${T - 6}" font-size="11" fill="var(--dim)">${esc(pts[0].at)}</text>
      <text x="${x(pts.length - 1)}" y="${T - 6}" font-size="11" text-anchor="end" fill="var(--dim)">${esc(pts[pts.length - 1].at)}</text></svg></div>`;
  }

  html += '<details><summary>Every recorded snapshot</summary><div class="scroll"><table><tr><th>When</th><th>Commit</th><th class="num">Smells</th><th class="num">Pressure</th><th class="num">Analyzers</th><th>Change that moved it</th></tr>' +
    [...H].reverse().map(s => `<tr><td>${esc(s.At)}</td><td class="mono">${esc(s.Commit)}</td><td class="num">${fmt(val(s, 'Smells', 'Total'))}</td><td class="num">${fmt(val(s, 'Smells', 'Pressure'))}</td><td class="num">${fmt(val(s, 'Analyzers', 'Total'))}</td><td>${esc(s.Note || '')}</td></tr>`).join('') + '</table></div></details>';
  $('trend').innerHTML = html;
})();

// hotspots
const byFile = {};
F.forEach(f => { const o = byFile[f.file] || (byFile[f.file] = { file: f.file, findings: 0, score: 0, lines: 0 }); o.findings++; o.score += f.weight; });
D.files.forEach(f => { if (byFile[f.file]) byFile[f.file].lines = f.lines; });
const hot = Object.values(byFile).map(o => ({ ...o, score: Math.round(o.score * 10) / 10 })).sort((a, b) => b.score - a.score).slice(0, 40);
const maxScore = hot.length ? hot[0].score : 1;
sortable($('hot'), hot, [
  { k: 'file', h: 'File', f: r => `<span class="mono">${esc(r.file)}</span>` }, { k: 'lines', h: 'Lines', num: true },
  { k: 'findings', h: 'Findings', num: true }, { k: 'score', h: 'Pressure', num: true },
  { k: 'score', h: '', f: r => `<div class="bar" style="width:${Math.max(2, 140 * r.score / maxScore)}px"></div>` }]);

// rules
const ruleRows = Object.entries(D.rules).map(([id, r]) => ({ id, ...r, n: F.filter(f => f.rule === id).length })).sort((a, b) => b.n - a.n);
$('rules').innerHTML = ruleRows.map(r => `<details><summary>${esc(r.title)} <span class="tag">${esc(r.id)}</span> <span class="tag">${r.n}</span> <span class="tag ${r.automation}">${r.automation}</span></summary>
  <div class="panel"><b>Why it matters.</b> ${esc(r.why)}<br><b>What to do.</b> ${esc(r.fix)}</div></details>`).join('');

// biggest problems
const biggest = [...F].sort((a, b) => b.weight - a.weight).slice(0, 25);
sortable($('top'), biggest, [
  { k: 'weight', h: 'Weight', num: true }, { k: 'rule', h: 'Rule' }, { k: 'file', h: 'Where', f: r => `<span class="mono">${esc(r.file)}:${r.line}</span><br>${esc(r.member)}` },
  { k: 'message', h: 'What' }, { k: 'automation', h: 'Fix', f: r => `<span class="${r.automation}">${r.automation}</span>` }]);

// all findings
$('fr').innerHTML = '<option value="">any rule</option>' + Object.keys(D.rules).sort().map(r => `<option>${esc(r)}</option>`).join('');
function drawAll() {
  const q = $('q').value.toLowerCase(), fr = $('fr').value, fs = $('fs').value, fa = $('fa').value;
  const rows = F.filter(f => (!fr || f.rule === fr) && (!fs || f.status === fs) && (!fa || f.automation === fa) && (!q || (f.file + ' ' + f.member + ' ' + f.message).toLowerCase().includes(q)));
  $('count').textContent = `${rows.length} of ${F.length}` + (rows.length > 400 ? ' (showing the first 400)' : '');
  sortable($('all'), rows.slice(0, 400), [
    { k: 'rule', h: 'Rule' }, { k: 'file', h: 'Where', f: r => `<span class="mono">${esc(r.file)}:${r.line}</span><br>${esc(r.member)}` },
    { k: 'value', h: 'Value', num: true }, { k: 'message', h: 'What' }, { k: 'status', h: 'Baseline', f: r => `<span class="${r.status}">${r.status}</span>` }]);
}
['q', 'fr', 'fs', 'fa'].forEach(id => $(id).oninput = drawAll); drawAll();

// crawl + scripts
let extra = '';
if (D.scripts) extra += `<h2>Browser scripts</h2><pre>${esc(D.scripts)}</pre>`;
if (D.crawl) {
  extra += `<h2>Site crawl: ${esc(D.crawl.url)}</h2><div class="dim">${D.crawl.pages} pages. Same problem on many pages is one row.</div><div class="scroll"><table id="crawl"></table></div>`;
}
$('extra').innerHTML = extra;
if (D.crawl) {
  const groups = {};
  D.crawl.issues.forEach(i => { const k = i.severity + '|' + i.text; (groups[k] = groups[k] || { severity: i.severity, text: i.text, pages: [] }).pages.push(i.page); });
  const rows = Object.values(groups).map(g => ({ severity: g.severity, text: g.text, n: g.pages.length, where: g.pages.slice(0, 3).join(', ') + (g.pages.length > 3 ? ', ...' : '') }));
  sortable($('crawl'), rows, [{ k: 'severity', h: 'Level', f: r => `<span class="${r.severity === 'Error' ? 'new' : 'dim'}">${r.severity}</span>` }, { k: 'text', h: 'Problem' }, { k: 'n', h: 'Pages', num: true }, { k: 'where', h: 'Where', f: r => `<span class="mono">${esc(r.where)}</span>` }]);
}
</script></body></html>
""";
}
