# CodeQuality

Private tooling from 71 Degrees that keeps a .NET codebase from drifting. It was built for the TriviaNight / Better Trivia site and is meant to be reused on other sites of the same kind (ASP.NET Core,
Blazor, a front end that is Node-free or lightly scripted). Everything is read-only unless a command says otherwise, runs on demand, needs no AI, and writes a self-contained HTML report.

| Tool | Project | What it does |
|---|---|---|
| **Quality** | `src/Quality` | Code smells (Roslyn over the C# and Razor), .NET analyzers at "Recommended", browser-script checks, a Playwright site crawl, a baseline ratchet, a quality timeline, and the HTML report |
| **Refactor** | `src/Refactor` | Roslyn-driven mechanical fixes: `fix-analyzers`, `extract-block`, `lambdas-to-methods`, `log-empty-catches` (dry run unless `--apply`; needs a clean git tree) |
| **Common** | `src/Common` | Shared helpers: finding the repository, running a process, reading `quality/quality.json` |

## Using it in a site repository

Add this repository as a submodule so each site pins the version it was checked with:

```
git submodule add https://github.com/71-Degrees/CodeQuality.git tools/CodeQuality
git submodule update --init
```

Run from the **root of the site repository** (the tools find it by the nearest `.git` above the working directory):

```
dotnet run --project tools/CodeQuality/src/Quality                     # smells + analyzers + script checks (about a minute)
dotnet run --project tools/CodeQuality/src/Quality -- smells           # seconds, no build
dotnet run --project tools/CodeQuality/src/Quality -- analyzers        # a full build with the CA*/IDE* analyzers
dotnet run --project tools/CodeQuality/src/Quality -- scripts          # the npm checks named in scriptChecks
dotnet run --project tools/CodeQuality/src/Quality -- crawl            # headless Chromium walk of crawl.defaultUrl (or --url)
dotnet run --project tools/CodeQuality/src/Quality -- tests            # every test project with coverage (--include-browser adds the browser tests)
dotnet run --project tools/CodeQuality/src/Quality -- quick            # small iterations: smells of just the changed files + just the tests that mention them (--since origin/master, --no-tests, --no-build)
dotnet run --project tools/CodeQuality/src/Quality -- history          # the timeline summary (--backfill rebuilds it from git)
dotnet run --project tools/CodeQuality/src/Quality -- smells --update-baseline   # accept today's state (after fixing things, or on purpose)
dotnet run --project tools/CodeQuality/src/Refactor -- fix-analyzers   # dry run of the mechanical analyzer fixes
```

**`quick` - the fast check between edits.** It looks only at what git says changed (staged, unstaged and new files against HEAD, or `--since <ref>` such as `origin/master` for everything since the last push), runs the smell scan and keeps just those files' findings against the baseline (new / worse / fixed), then runs only the tests that are likely to touch the change: the type names a changed file declares are looked up in the test projects' source and every test class in a file that mentions one is run (`dotnet test --filter`); a changed Razor component is followed up to the pages that show it and the tests that request those pages' routes are added (the usual shape of tests that host the real app); a changed test file runs its own classes. Changed files no test mentions are named, so a gap shows. It writes nothing - no report, no timeline entry, no baseline change - so a partial look never stands in for a full one. It is a shortcut by name, not a proof (a test reaching the code only through the running app can be missed, and a widely shared component selects widely), so the full `quality` run and the whole test suite stay the gate before a push. Exit 0 clean, 1 something new/worse/failing, 2 a check could not run.

Add `--open` to any Quality run to open `reports/quality-latest.html` in Chrome, or set `"openReport": "always"` (every run) or `"onFindings"` (only when something is new or worse) in `quality/quality.json` so each run puts the report in front of you; `--no-open` overrides the setting for scripted runs. `quality features` writes `reports/feature-map.html`: every routable page (from the `@page` directives), how many clicks it is from the home page by following the links in the markup (a page's own, its layout's and its components'), which tests mention it (unit/web, component or browser) and whether the site crawler visits it (public, reachable by links and not skipped). It is worked out from the code each time, so it cannot go stale. A site can also keep a curated list of its functions (`quality/features.json`: what a person does, the steps and clicks, who may do it, the pages it uses, the test classes that cover it); the map checks the list against the code every run - a page that belongs to no function, a route that is not a page, a test named that does not exist, and test classes no function names are all reported - and shows each function as not covered, covered by unit/web/component tests, or covered by a browser test. Every "Click ..." step in the list must name text that appears on a page (a step describing a control nobody can click is flagged). Behind each function it shows what its pages ask for (`@inject`ed services and stores, each with its class from the performance notes: Cached, Uncached, HttpCached), how many commits touched its source in the last 90 days (a function that keeps changing and has no browser test is flagged), the docs that name its pages as code (plus any the list names itself), and whether its source sits on a path that decides who can do what or takes payment (the release panel's `sensitivePaths`); `features.signedInPrefixes` in `quality/quality.json` says which routes need a signed-in visitor.

Every run also rewrites `reports/quality-dashboard.html`, a fixed-name page that draws the timeline (`quality/history.json`, one value set per commit) and the push timings (`reports/check-timings.jsonl`, when the site has them) as trends: smells and refactoring pressure against where they started, tests, coverage, analyzer findings and how long a push waited. The report links to it. A commit that only had the quick unit-test run shows no test numbers rather than a dip. Exit code: 0 clean, 1 something new or worse (or a crawl error), 2 a check could not run. It also shows the feature map's progress: a card (functions walked in a browser, of those listed) and a chart of functions listed, with a browser test of some kind, and walked, at every commit that changed `quality/features.json`. Those numbers are read back from git (each commit's copy of the list), so nothing extra is recorded and the history is complete from the first commit of the list.

**First run - `setup`.** A site that has just got Code Quality has a dozen once-only things to do besides the baselines, and nobody remembers them all. `quality setup` works out which are still missing from the repository's own state (nothing to tick): the smells and analyzer baselines, the smells ratchet in the push gate, `reports/` ignored, test coverage, the feature map and `features.json`, the analyzers switched on in `Directory.Build.props`, the scripts check (only when there is a `package.json`), the staging crawl, browser tests, the release panel and the push timings. It lists them best value for the time first - value 1-5, minutes to run, minutes to set up, what runs it (the .NET tool, a build or test run, Node, a browser, or Claude editing files), and the token cost to the owner (a low-high estimate scaled to the site's size, as a percentage of one 200k-token session, so "does Claude burn tokens or does a .NET executable do the work" has a number) and, only when it is heavy, the machine cost - with anything that touches a live site (the crawl) marked NEEDS THE OWNER'S OK and put last. Every normal run ends with it: a "FIRST RUN" block while the site has no `quality/setup.json`, then a one-line reminder while anything is outstanding. `setup --skip <id> --reason "..."` records a deliberate no (committed, so the reminder stops for it), `setup --acknowledge` records that the list was reviewed. It never changes an exit code, so it cannot fail a push. The output is written for Claude to read: it says to tell the user what is missing, ask which to do now, and not run an approval item without their OK (the skill says the same).

First time in a site: run `smells` and `analyzers` with `--update-baseline`, then commit `quality/baseline.json`, `quality/history.json` and `quality/quality.json`. Add `reports/` to the site's `.gitignore`.
If the site already has a `quality/baseline.json` history in git, `history --backfill` rebuilds the timeline from it.

Upgrading a site: `git submodule update --remote tools/CodeQuality`, build, run the checks, commit the new submodule pointer.

## Per-site settings: `quality/quality.json`

Every setting is optional. With no file the tools check `src` and `tools` of the one solution at the root, run no script checks and have no crawl address.

```json
{
  "siteName": "Better Trivia",
  "solution": "TriviaNight.slnx",
  "sourceFolders": [ "src", "tools" ],
  "testFolders": [ "tests" ],
  "skipFolders": [ "/Migrations/" ],
  "scriptChecks": [ "typecheck", "lint" ],
  "crawl": { "defaultUrl": "https://staging.example.com", "userAgent": "ExampleQualityCrawler/1.0", "skipPattern": "^/(account|admin|api)" },
  "ignoredAnalyzerRules": { "CA1707": "tests are named as sentences" },
  "disabledRules": [ "DirectHttpClient" ],
  "ruleExemptions": { "DirectClock": [ "/Startup/" ] },
  "hotspotDays": 90,
  "limits": { "methodLines": 60, "complexity": 15, "nesting": 4, "parameters": 7, "fileLines": 700, "razorCodeLines": 400, "duplicateTokens": 70, "duplicateLines": 8 },
  "toolCommand": "dotnet run --project tools/CodeQuality/src/Quality --",
  "extraCommands": [ "dotnet run --project tools/Deploy -- check     the full pre-push check" ]
}
```

`scriptChecks` are npm script names (`npm run <name>`); the crawler uses axe-core from `node_modules` when it is there. The analyzer rules should also be switched on in the site's own
`Directory.Build.props` (`AnalysisMode=Recommended`, `CodeAnalysisTreatWarningsAsErrors`); copy this repository's `Directory.Build.props` and `.editorconfig` as a starting point.

## What the checks do

- **Smells:** long methods, branchy methods (decision points), deep nesting, too many parameters, big files, big Razor `@code` blocks, empty and catch-all `catch`, `async void`, blocking on a task,
  TODO/FIXME/HACK markers. A smell is a lead for a person, not a verdict.
- **Analyzers:** a full build into a scratch folder (so the tool never tries to overwrite its own running files) with the .NET analyzers at Recommended; warnings are counted per rule and file.
- **The ratchet:** `quality/baseline.json` (committed) records what exists; a run fails only on what is new or worse, so old code is cleaned up at leisure and new code cannot add to the pile.
  Findings are keyed by rule + file + member, never a line number. The baseline only ever shrinks unless someone deliberately accepts something (say why in the commit).
- **Timeline:** `quality/history.json` (committed) gets a snapshot whenever a run's numbers change: smell count, "refactoring pressure" (how far over its limit each smell is, summed) and analyzer
  findings, per rule. The report opens with start / before the last change / now, a by-rule table and a chart.
- **Standard-as-rules:** smells for rules a team writes down and then has to remember: `MutableStatic` (static state that can change), `DirectClock` (`DateTime.UtcNow` and friends; take a `TimeProvider`),
  `DirectHttpClient` (`new HttpClient()`; take a factory), `DroppedTask` (`_ = DoAsync()`), and in tests `TestWithoutAssert` and `SkippedTest`. The clock, HttpClient and dropped-task rules are one finding per
  file whose value is the count. Switch a rule off with `disabledRules`, or exempt the edges of the app with `ruleExemptions` (`{ "DirectClock": [ "/Startup/" ] }`).
- **Duplicate code (`DuplicateCode`):** every window of `limits.duplicateTokens` (70) consecutive tokens, with names and literals ignored, is hashed; a hash found in two places marks the code as repeated, and a run of
  at least `limits.duplicateLines` (8) lines is one finding naming where else it is. Auto-property lists, enums, usings and attributes are ignored, as is a long stretch of similar lines inside one file.
- **Hotspots:** the report ranks files by commits over the last `hotspotDays` (90) times their pressure: the code that is both hard to change and being changed, so the cheapest place to start refactoring.
- **Newer checks and the timeline:** rules added after the first recording are listed in `Rules.Newer`; the timeline's headline numbers leave them out (so adding a check never looks like quality getting worse) and
  they get their own row.
- **Crawl:** Playwright, phone-sized by default, read-only; broken links, console and network errors, SEO basics, sideways scrolling, images with no alt, axe-core accessibility.
- **Tests and coverage:** `quality tests` runs each test project (`testProjects` in quality.json, else every project one folder down under the test folders, browser tests only with `--include-browser`) with the coverage collector; the report also reads whatever the test projects last wrote to `reports/test-results/<project>` (a TRX and a Cobertura file each) on every run, so a site whose pre-push check runs its tests with the same arguments never has a report more than a push old. It shows the number of tests, failures, and line coverage per project folder (our own C# source only: Razor components are compiled into generated code the coverage cannot map back), and the timeline records them. Test projects need the `coverlet.collector` package.
  One-time: `pwsh src/Quality/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Browser tests (`src/Quality.Browser`)

The part of a site's browser tests that every site shares, so a site writes only its scenarios. It starts the site as its own process on a free port (`SiteHost`), launches Playwright's Chromium, gives each test
its own browser context with a phone-sized viewport, and listens to the page (`PageProbe`): a console error, an uncaught page error or a 5xx response fails the test even when its visible assertions passed.
`BlazorPages.GotoLiveAsync` waits until a Blazor Server page is actually live (runtime started, no error or reconnect bar), and `ShowsBlazorErrorAsync` reports a crashed circuit.

A site adds an xunit project that references `src/Quality.Browser` and subclasses `BrowserSiteFixture`, saying only how its site starts (`SiteHostOptions`: project, build configuration, environment such as storage folders
and switches that keep tests off outside services) and where the run log goes. Mark tests `[BrowserFact]`: they are skipped, with the reason, where Chromium is not installed (install it once with
`pwsh <a built Playwright project>/bin/Debug/net10.0/playwright.ps1 install chromium`) or when `QUALITY_BROWSER=0`. `QUALITY_BROWSER_HEADED=1` shows the browser, slowed down, so a person can watch.

Data is the site's own and is meant to persist: point the site at a storage folder that is kept between runs and never cleaned up, and write scenarios that tolerate what an earlier run left (unique names,
no assumption of an empty store). `BrowserRunLog` appends one JSON line per scenario (when, name, milliseconds, problems) and never rewrites the file, so slippage shows over many runs: a start that doubled, a
scenario that got slow.

## Working on the tools

`dotnet build CodeQuality.slnx` and `dotnet test CodeQuality.slnx`. The tools check themselves: `dotnet run --project src/Quality`. See `CLAUDE.md` for the engineering standard.
These tools lived in `tools/Quality`, `tools/Refactor` and `tools/Common` of the TriviaNight repository until 2026-10-09.

## The Claude Code skill

`skill/code-quality/SKILL.md` teaches Claude Code the workflow (set a site up, run and read the checks, act on the ratchet, use the Refactor tool, change the tools). Install it once per machine so it is available
in every site (the repository copy is the source of truth; copy again after changing it):

```
mkdir -p ~/.claude/skills/code-quality && cp skill/code-quality/SKILL.md ~/.claude/skills/code-quality/SKILL.md
```
