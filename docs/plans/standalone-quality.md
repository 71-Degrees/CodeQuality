# CodeQuality as one machine-wide tool (plan, 2026-11-24)

**The owner's ask (2026-11-24):** CodeQuality sits outside any one repo, as its own thing, installed once and run against every
repo. TypeScript and ESLint are machine-wide tools that CodeQuality owns. They are installed once, kept current on the fly and follow the
same rule as the .NET SDK: a floor on the major version, minor and patch releases taken automatically, never a new major unasked. A repo
that has no browser scripts yet is still covered the day it gets some. Haiku does the mechanical work.

## Where it stands today (mapped 2026-11-24)

- **CodeQuality is a git submodule** at `tools/CodeQuality` in all seven other repos. It is started by path
  (`dotnet run --project tools/CodeQuality/src/Quality --`) from:
  - the WebCore deploy engine's push check (`WebCore.Deploy/QualityChecks.cs`, smells only), with "submodule missing, not
    blocking" as its fallback;
  - MyWave's own copy of that check (`tools/Deploy/Classic/QualityChecks.cs`);
  - TriviaNight's `ship check` (`tools/Ship/CheckCommand.cs:33`);
  - the `changedTests`, `background` and `full[]` commands in TriviaNight's and MyWave's `deploy/deploy.json`;
  - the edit hook `claude-config/hooks/quality-on-edit.ps1`, which finds a repo by probing for the submodule and silently does
    nothing without it;
  - the `code-quality`, `haiku-build` and `new-dotnet-project` skills.
- The tool already works on whatever repo is the current folder (`Repo.FindRoot`, settings in `<repo>/quality/quality.json`), so
  nothing in it needs its own location to be inside the repo.
- **CodeQuality has no publish or install command.** Playwright's install path assumes the Debug build output.
- **Script checks** run `npm run typecheck` and `npm run lint` inside the target repo. That needs the repo's own `package.json`,
  `node_modules` and `eslint.config.mjs`, and only TriviaNight and CrateDigger have them (TypeScript ^7.0.2, ESLint ^10.12.0).
  The crawler needs `node_modules/axe-core` in the repo, and only TriviaNight has it. The push check probes
  `npx --no-install tsc` and, if it is missing, skips the check silently. Only TriviaNight wires the scripts into the push check.
- **The two ESLint settings files are nearly identical:** the same rules, apart from three that only one repo has
  (`prefer-const`, `no-var`, `no-empty`), and different page globals (`triviaNight` or `cratedigger`, `L`, `Blazor`). The
  `tsconfig.json` files differ only in the ES target and stay with the repo: they list its files.
- **`Quality.Browser` is a library, not just a command.**
  - It is compiled into 8 projects: the browser tests in TriviaNight, CrateDigger and MyWave, TriviaNight's Canary and AnswerWatch,
    and two AiVisibility projects. Quality's own crawler also uses it.
  - The browser-test generator writes that path into new projects, and four `.slnx` files list it.
  - Publishing the tool does not replace a compile-time reference: see decision D1.
- No `Directory.Build.*` file references CodeQuality, and the analyzers are the SDK's own, so builds do not depend on the
  submodule beyond `Quality.Browser`.

## The design

1. **One published copy**, `C:\71Degrees\published\quality` (outside AppData, like Ship), built from `C:\71Degrees\repos\CodeQuality`:
   - `bin\` holds Quality, and `refactor\` holds the Roslyn refactor tool.
   - `node\` holds the script tools and their settings: `package.json` and `package-lock.json` (committed in CodeQuality under
     `node/`), with `typescript ^7`, `eslint ^10`, `@eslint/js ^10`, `globals ^17` and `axe-core ^4`.
   - It also holds the Playwright browser for the crawler.
2. **`quality install`** (new, in CodeQuality):
   - refuses while a quality run is in progress;
   - pulls CodeQuality, publishes both tools Release into a fresh folder and swaps it in (a running copy is never overwritten);
   - runs `npm ci` and then `npm update` in `node\`, which takes minor and patch releases within each major (the SDK rule) and never a
     new major;
   - writes `installed.json` (commit, date, tool versions).

   `quality install --check` prints whether the copy is behind CodeQuality's origin, without changing anything.
3. **Kept current on the fly:** a Windows task, `CodeQuality refresh`, runs `quality install` daily at 01:45, before the nightly
   jobs. The Doctor reports a missing or out-of-date copy.
4. **CodeQuality owns the script checks.**
   - `quality scripts` runs `tsc -p <the repo's tsconfig>` and `eslint --config <node>\eslint.config.mjs <the repo's wwwroot/js>`,
     both from `node\`.
   - One shared `eslint.config.mjs` holds the union of today's rules. A repo's page globals come from `quality.json`
     (`"scripts": { "globals": { ... } }`).
   - The script folder (`src/*/wwwroot/js`, without `lib/`) and the tsconfig beside it are found automatically. A repo that gains
     browser scripts is checked with no setup, and a repo without them reports "no browser scripts".
   - The crawler reads `axe-core` from `node\`.
5. **Every caller runs the published copy**: `dotnet C:\71Degrees\published\quality\bin\Quality.dll <command>`, through one
   resolver in each of the two deploy engines.
   - The push check runs smells and scripts through it.
   - **A missing copy fails the push check with the install command**, instead of passing silently (decision D3).
   - The hook finds a repo by its `quality/quality.json` and runs the published copy.
   - The skills, the `quality.json` `toolCommand` values and the docs say the new command.
6. **The per-repo copies are switched off, not deleted:**
   - TriviaNight's and CrateDigger's `package.json`, `eslint.config.mjs` and `node_modules` are no longer used.
   - TriviaNight's `scriptTypeCheck` and `scriptLint` settings give way to the built-in call.
   - The files stay until the owner says remove.
   - The `tools/CodeQuality` submodule stays only for the `Quality.Browser` library (D1).

## Decisions (recommended defaults; the owner may overturn any)

- **D1, the `Quality.Browser` library. Recommended: keep the submodule for now, used only by the browser-test projects.**
  - The alternative: publish `Quality.Browser` as a package into a local folder feed (`C:\71Degrees\published\packages`), mapped to
    `CodeQuality.*` only, and drop the submodule everywhere.
  - That alternative changes the "nuget.org only" rule in every `nuget.config` and the lock files, so it is its own decision and
    its own slice (S6).
- **D2, one ESLint rule set. Recommended: the union of today's rules** (`prefer-const`, `no-var` and `no-empty` everywhere). Any new
  findings it raises in TriviaNight or CrateDigger are fixed in S2.
- **D3, a missing published copy fails the push check, with the install command. Recommended.** The current behaviour (passing
  silently) means a machine without the tool pushes unchecked code.

## Slices

| # | Slice | Builder | Repos | Cost (session / week SP) |
|---|---|---|---|---|
| S1 | `quality install` and `--check`, the `node/` package files, a Release publish with the swap, the Playwright path, `installed.json`, tests | Opus designs, Haiku builds | CodeQuality | 3 / 1.3 |
| S2 | `quality scripts` from `node\`: the shared ESLint settings, globals from `quality.json`, auto-detection, axe-core path, tests; fix any new findings | Opus | CodeQuality, TriviaNight, CrateDigger | 3 / 1.3 |
| S3 | The callers run the published copy: WebCore `QualityChecks` (smells and scripts, loud failure), MyWave's Classic copy, Ship `CheckCommand`, the `deploy.json` and `quality.json` strings, tests | Haiku | WebCore, MyWave, TriviaNight, the other app repos (pointer bump) | 2 / 0.9 |
| S4 | The edit hook, the three skills, the instruction lines in each `CLAUDE.md` and the docs that give the command | Haiku | claude-config, all repos | 1.5 / 0.7 |
| S5 | The `CodeQuality refresh` task, the Doctor check, the switch-off of the per-repo script packages; verify a push check on TriviaNight and CrateDigger finds what today's does; push everything | Opus | all | 2 / 0.9 |
| S6 | (only on the owner's word, D1) `Quality.Browser` as a local package, the submodule removed | later | all | 4 / 1.8 |

**Total for S1-S5:** about 11.5 session SP and 5 week SP.

**Order:** S1 and S2 come first, since everything else calls them. S3 and S4 can then run in either order. S5 ends with the
verification. Each slice is pushed when its own checks pass; the repos keep working on the submodule until S3 lands.
