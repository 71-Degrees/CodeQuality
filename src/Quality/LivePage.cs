/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>
/// The page a run opens in Chrome: static, no server. Every two seconds it loads <c>status.js</c> (a script, because a page opened from disk may load scripts but not fetch files), redraws from it, and stops
/// polling once the run has ended. It keeps no state of its own, so a refresh or a second tab shows the same thing.
/// </summary>
internal static class LivePage
{
    internal const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Quality run</title>
<style>
:root { --bg:#f6f7f9; --card:#fff; --ink:#1b1f24; --mute:#667085; --line:#e3e6ea; --ok:#1a7f37; --bad:#c62828; --run:#0b63ce; --warn:#b26a00; }
@media (prefers-color-scheme: dark) { :root { --bg:#14171b; --card:#1e2227; --ink:#e8eaed; --mute:#9aa3ad; --line:#2f353c; --ok:#4cc26b; --bad:#ff6b6b; --run:#6aa9ff; --warn:#e0a040; } }
* { box-sizing: border-box; }
body { margin:0; padding:20px 16px 48px; background:var(--bg); color:var(--ink); font:15px/1.45 system-ui, "Segoe UI", sans-serif; }
main { max-width:900px; margin:0 auto; }
.banner { background:var(--card); border:1px solid var(--line); border-left:8px solid var(--run); border-radius:8px; padding:16px 18px; }
.banner.passed { border-left-color:var(--ok); } .banner.failed, .banner.error { border-left-color:var(--bad); }
.banner h1 { margin:0; font-size:22px; } .banner p { margin:4px 0 0; color:var(--mute); }
.bar { height:10px; background:var(--line); border-radius:5px; margin-top:12px; overflow:hidden; } .bar > i { display:block; height:100%; background:var(--run); transition:width .5s; }
.passed .bar > i { background:var(--ok); } .failed .bar > i, .error .bar > i { background:var(--bad); }
h2 { font-size:15px; margin:22px 0 8px; color:var(--mute); text-transform:uppercase; letter-spacing:.04em; }
table { width:100%; border-collapse:collapse; background:var(--card); border:1px solid var(--line); border-radius:8px; overflow:hidden; }
td { padding:9px 12px; border-top:1px solid var(--line); vertical-align:top; } tr:first-child td { border-top:0; }
td.n { width:1%; white-space:nowrap; text-align:right; color:var(--mute); font-variant-numeric:tabular-nums; }
.dot { display:inline-block; width:1.3em; font-weight:700; } .running .dot { color:var(--run); } .passed .dot { color:var(--ok); } .failed .dot, .error .dot { color:var(--bad); } .pending, .skipped { color:var(--mute); }
.sub { display:block; color:var(--mute); font-size:13px; } .sub.bad { color:var(--bad); }
.f { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:8px 12px; margin:6px 0; font-size:14px; word-break:break-word; }
.tag { display:inline-block; font-size:12px; font-weight:700; padding:0 7px; border-radius:9px; color:#fff; margin-right:6px; } .tag.new { background:var(--bad); } .tag.worse { background:var(--warn); }
.stale { color:var(--warn); font-weight:600; }
footer { margin-top:22px; color:var(--mute); font-size:13px; }
</style>
</head>
<body>
<main>
  <div id="banner" class="banner"><h1>Waiting for the run...</h1><p>This page reads status.js beside it every 2 seconds.</p></div>
  <h2>Steps</h2>
  <table id="stages"></table>
  <h2 id="fh">Findings against the baseline</h2>
  <div id="findings"></div>
  <footer id="foot"></footer>
</main>
<script>
var last = null, timer = null;
function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; }
function secs(a, b) { return Math.max(0, ((b ? new Date(b) : new Date()) - new Date(a)) / 1000); }
function dur(s) { s = Math.round(s); return s >= 60 ? Math.floor(s / 60) + 'm ' + String(s % 60).padStart(2, '0') + 's' : s + 's'; }
var MARK = { pending: '·', running: '▶', passed: '✓', failed: '✗', error: '!', skipped: '–' };

function progress(d) {
  var known = d.stages.map(function (s) { return s.estimateSeconds; }).filter(Boolean);
  var fallback = known.length ? known.reduce(function (a, b) { return a + b; }, 0) / known.length : 30;
  var total = 0, done = 0;
  d.stages.forEach(function (s) {
    var w = s.estimateSeconds || fallback; total += w;
    if (s.state === 'passed' || s.state === 'failed' || s.state === 'error') done += w;
    else if (s.state === 'running') done += w * Math.min(secs(s.startedAt) / w, 0.95);
  });
  return d.state !== 'running' ? 100 : total ? Math.min(99, done / total * 100) : 0;
}

function renderBanner(d) {
  var b = document.getElementById('banner');
  b.className = 'banner ' + d.state;
  b.replaceChildren();
  var running = d.stages.filter(function (s) { return s.state === 'running'; })[0];
  var finished = d.stages.filter(function (s) { return ['passed', 'failed', 'error'].indexOf(s.state) >= 0; }).length;
  b.appendChild(el('h1', '', d.state === 'running' ? 'Running: ' + (running ? running.label : d.command) : d.verdict));
  var line = 'quality ' + d.command + ' - started ' + new Date(d.startedAt).toLocaleTimeString() + ' - ' + dur(secs(d.startedAt, d.finishedAt)) + (d.state === 'running' ? ' so far' : ' in all');
  if (d.state === 'running' && d.estimatedFinishAt) line += ' - about ' + dur(secs(new Date(), d.estimatedFinishAt) * (new Date(d.estimatedFinishAt) > new Date() ? 1 : 0)) + ' to go (from earlier runs)';
  b.appendChild(el('p', '', line));
  if (d.state !== 'running') b.appendChild(el('p', '', d.summary));
  var bar = el('div', 'bar'); var fill = el('i'); fill.style.width = progress(d) + '%'; bar.appendChild(fill); b.appendChild(bar);
  document.title = (d.state === 'running' ? finished + '/' + d.stages.length + ' running' : d.state === 'passed' ? 'Clear' : 'Needs a look') + ' - quality ' + d.command;
}

function stageRow(s) {
  var tr = el('tr', s.state);
  var name = el('td'); name.appendChild(el('span', 'dot', MARK[s.state] || '?')); name.appendChild(document.createTextNode(s.label));
  var sub = [];
  if (s.total != null) sub.push(s.passed + ' passed, ' + s.failed + ' failed, ' + s.total + ' tests');
  if (s.detail) sub.push(s.detail);
  if (sub.length) name.appendChild(el('span', 'sub' + (s.state === 'failed' || s.state === 'error' ? ' bad' : ''), sub.join(' - ')));
  tr.appendChild(name);
  var t = '';
  if (s.state === 'running') t = dur(secs(s.startedAt)) + (s.estimateSeconds ? ' of ~' + dur(s.estimateSeconds) : '');
  else if (s.startedAt && s.finishedAt) t = dur(secs(s.startedAt, s.finishedAt)) + (s.estimateSeconds ? ' (usually ' + dur(s.estimateSeconds) + ')' : '');
  else if (s.estimateSeconds) t = 'usually ' + dur(s.estimateSeconds);
  tr.appendChild(el('td', 'n', t));
  return tr;
}

function renderFindings(d) {
  var host = document.getElementById('findings'); host.replaceChildren();
  document.getElementById('fh').textContent = 'Findings against the baseline (' + d.findings.length + ')';
  if (!d.findings.length) host.appendChild(el('div', 'f', d.state === 'running' ? 'None so far.' : 'Nothing new or worse.'));
  d.findings.forEach(function (f) {
    var row = el('div', 'f'); row.appendChild(el('span', 'tag ' + f.kind, f.kind.toUpperCase()));
    row.appendChild(document.createTextNode(f.rule + '  ' + f.file + ':' + f.line + '  ' + f.message)); host.appendChild(row);
  });
}

function render(d) {
  last = d;
  renderBanner(d);
  var t = document.getElementById('stages'); t.replaceChildren(); d.stages.forEach(function (s) { t.appendChild(stageRow(s)); });
  renderFindings(d);
  var foot = document.getElementById('foot'); foot.replaceChildren();
  var age = secs(d.updatedAt);
  if (d.state === 'running') {
    if (age > 30) foot.appendChild(el('span', 'stale', 'No update for ' + dur(age) + ' - the run may have stopped. '));
    foot.appendChild(document.createTextNode('Updated every 2 seconds while the run goes.'));
  } else {
    foot.textContent = 'The run has ended; this page stopped polling. Run quality again for a new one.';
    clearInterval(timer);
  }
}

function poll() {
  var s = document.createElement('script');
  s.src = 'status.js?t=' + Date.now();
  s.onload = function () { s.remove(); if (window.QUALITY_LIVE) render(window.QUALITY_LIVE); };
  s.onerror = function () { s.remove(); };
  document.head.appendChild(s);
}
timer = setInterval(poll, 2000);
setInterval(function () { if (last && last.state === 'running') render(last); }, 1000);
poll();
</script>
</body>
</html>
""";
}
