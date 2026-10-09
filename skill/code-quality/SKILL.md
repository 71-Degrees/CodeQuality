---
name: code-quality
description: Run, read and act on the 71 Degrees CodeQuality tooling (Roslyn code smells, .NET analyzers, baseline ratchet, quality timeline, HTML report, site crawler, Roslyn refactor tool) in a site repository, or set it up in a new site. Invoke when the user asks for the quality report or the quality check, wants to know where quality stands (start vs now), is about to start or just finished a large refactor or feature, asks to apply mechanical refactors or analyzer fixes, wants to crawl the site for accessibility/SEO/broken links, or wants CodeQuality added to another website.
allowed-tools: Bash, Read, Edit, Write, Glob, Grep
---

The tools live in the private repository `71-Degrees/CodeQuality` (local copy `C:\71Degrees\repos\CodeQuality`) and are consumed by each site as the git submodule `tools/CodeQuality`. Its `README.md` is the
full guide: **read it when setting a site up or when a flag is unfamiliar**. This file is the workflow. Everything runs from the **root of the site repository** (the working directory must be inside that git repo).

## 0. Is the site set up?

Check, from the site root: `tools/CodeQuality/src/Quality/Quality.csproj` exists (else `git submodule update --init`, or for a site that has never used it follow README "Using it in a site repository":
`git submodule add https://github.com/71-Degrees/CodeQuality.git tools/CodeQuality`), and `quality/quality.json` exists (else create it from the README template - ask the user for the site name, solution file, source and
test folders, npm script checks and the staging URL if unknown; add `/tools/CodeQuality/` to `skipFolders` because the submodule sits under `tools`). First baseline in a new site: run `smells` and `analyzers` with
`--update-baseline`, commit `quality/baseline.json`, `quality/history.json`, `quality/quality.json`, and put `reports/` in `.gitignore`. Use the command prefix from `toolCommand` in `quality/quality.json`
(normally `dotnet run --project tools/CodeQuality/src/Quality --`).

## 1. Run and read

- Quick look (seconds, no build): `<tool> smells`. Full set (about a minute): `<tool>` with no command = smells + analyzers + script checks. Add `--open` only if the user wants Chrome opened.
- Read the output, not just the exit code. Exit 0 clean, 1 something new or worse (or a crawl error), 2 a check could not run (say which and why; do not call it clean).
- Report the numbers: findings per check, **new / worse / fixed against the baseline**, and the one-line trend the tool prints ("code smells A -> B, refactoring pressure X -> Y, analyzer findings P -> Q").
  The HTML report (`reports/quality-latest.html`, dated copies alongside) opens with start / before-the-last-change / now, a by-rule table and a chart; when the user wants to see it, send that file
  (SendUserFile) rather than describing it. The timeline is `quality/history.json`; `<tool> history` prints the summary and `history --backfill` rebuilds it from the git history of the baseline.
- Do not run the crawler (`<tool> crawl`) casually: it is real traffic to `crawl.defaultUrl` (staging). Use a small `--max-pages` (8-20) unless the user wants the full walk, never point it at production
  unasked, and treat what it finds (broken links, console errors, SEO, axe-core accessibility) as leads to verify.

## 2. Act on it (the ratchet)

- New or worse findings are defects: fix them in the same change as the code that caused them, then re-run. Touch a baselined method or file and leave it better.
- `--update-baseline` only after the run shows **0 new, 0 worse** (the baseline only shrinks), or when the owner has explicitly decided to accept something - and then say why in the commit message.
  Commit `quality/baseline.json` and `quality/history.json` with the work that moved them.
- Fix by extracting, not by raising limits: method 60 lines, 15 decision points, nesting 4 (guard clauses), 7 parameters (group into a record), file 700 lines, Razor `@code` 400 lines. Changing `limits`
  in `quality/quality.json` is an owner decision, not a fix.
- Mechanical analyzer fixes and empty-catch logging go through the Refactor tool (`dotnet run --project tools/CodeQuality/src/Refactor -- fix-analyzers | extract-block | lambdas-to-methods | log-empty-catches`):
  dry run first (it is the default), clean git tree, then `--apply`, build, run the tests, review the diff, commit. One kind of change per commit. Do not write ad-hoc scripts (Python or otherwise) for edits the tool covers.
- A refactor is behaviour-preserving: tests before and after. A smell is a lead for a person, not a verdict - when a finding is a deliberate switch table or similar, say so rather than contorting the code.

## 3. Changing the tools themselves

The tools are site-agnostic: anything specific to one website goes in that site's `quality/quality.json`, never in the tool code. Work in `C:\71Degrees\repos\CodeQuality` (or inside the submodule), keep its own
checks at zero (`dotnet build CodeQuality.slnx`, `dotnet test CodeQuality.slnx`, `dotnet run --project src/Quality`), push (HTTPS remote; SSH push does not work from here), then in the site `git add tools/CodeQuality`
to move the pointer and commit. Pushing the tools repo is an ordinary green-build push; moving a site's pointer follows that site's own push rules.

## Gotchas

- Run from the site root; the tools find the repo by the nearest `.git`. The analyzer step builds the whole solution into a temp folder, so it works while the tool itself is running and while a dev server is up.
- Bash heredocs with apostrophes break; use the Write tool for files. Quote or avoid backslashes in `sed`; prefer the Edit tool for paths with `\`.
- The report is built by a Quality run; a run of one check keeps the other checks' last results (stamped with when they ran), so a quick `smells` does not erase the analyzer or crawl sections.
- When asked "where do we stand", answer with start -> now from the timeline, not only today's count.
