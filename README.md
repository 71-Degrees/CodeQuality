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
dotnet run --project tools/CodeQuality/src/Quality -- history          # the timeline summary (--backfill rebuilds it from git)
dotnet run --project tools/CodeQuality/src/Quality -- smells --update-baseline   # accept today's state (after fixing things, or on purpose)
dotnet run --project tools/CodeQuality/src/Refactor -- fix-analyzers   # dry run of the mechanical analyzer fixes
```

Add `--open` to any Quality run to open `reports/quality-latest.html` in Chrome. Exit code: 0 clean, 1 something new or worse (or a crawl error), 2 a check could not run.

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
  "limits": { "methodLines": 60, "complexity": 15, "nesting": 4, "parameters": 7, "fileLines": 700, "razorCodeLines": 400 },
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
- **Crawl:** Playwright, phone-sized by default, read-only; broken links, console and network errors, SEO basics, sideways scrolling, images with no alt, axe-core accessibility.
  One-time: `pwsh src/Quality/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Working on the tools

`dotnet build CodeQuality.slnx` and `dotnet test CodeQuality.slnx`. The tools check themselves: `dotnet run --project src/Quality`. See `CLAUDE.md` for the engineering standard.
These tools lived in `tools/Quality`, `tools/Refactor` and `tools/Common` of the TriviaNight repository until 2026-10-09.

## The Claude Code skill

`skill/code-quality/SKILL.md` teaches Claude Code the workflow (set a site up, run and read the checks, act on the ratchet, use the Refactor tool, change the tools). Install it once per machine so it is available
in every site (the repository copy is the source of truth; copy again after changing it):

```
mkdir -p ~/.claude/skills/code-quality && cp skill/code-quality/SKILL.md ~/.claude/skills/code-quality/SKILL.md
```
