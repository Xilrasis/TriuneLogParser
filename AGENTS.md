# Agent guidance — TriuneLogParser

This file is binding for any AI agent (and useful for any human) working in this
repository. Read it before making changes and follow it at all times.

## What this project is

A standalone Windows desktop application that parses **EverQuest combat logs from the
Project Triune server** (a multiclass server on the RoF2 client) in real time and
shows damage / healing / tanking breakdowns per player, encounter grouping, and an
always-on-top damage-meter overlay.

Project Triune's log wording differs from retail EQ and other emulators in small but
breaking ways, so mainstream parsers (EQLogParser, EQ Legends Companion) do not fully
work here. The authoritative description of the log grammar we support lives in
[`docs/log-format.md`](docs/log-format.md) — keep it current.

## Non-negotiables

1. **Standalone build.** Every release must produce a single-file, self-contained
   `win-x64` executable that runs with no .NET install and no browser:
   `dotnet publish src/TriuneLogParser.App -c Release -r win-x64 --self-contained \
   -p:PublishSingleFile=true`. Do not merge changes that break this or
   `.github/workflows/release.yml`.
2. **`TriuneLogParser.Core` stays UI-free.** No WPF / `System.Windows` references. It
   must remain unit-testable and usable from the CLI. UI code lives only in
   `TriuneLogParser.App`.
3. **Never commit real player logs.** The full sample logs stay on the user's machine.
   Only short, trimmed excerpts belong in `samples/` and `tests/fixtures/`.
4. **README and CHANGELOG travel with the code.** Any user-facing change updates
   [`README.md`](README.md) and adds a concise, high-level, feature-scoped entry to
   [`CHANGELOG.md`](CHANGELOG.md) under `## [Unreleased]` (newest first), in the same
   commit.

## Working on the parser

- Parsing is **rule-driven**: an ordered list of named regex rules in
  `src/TriuneLogParser.Core/Parsing/`, each turning one log line into a typed
  `CombatEvent`. Prefer adding/adjusting a rule or config over hard-coding behaviour
  in callers.
- The parser must stay **open-ended**. Unknown lines that look damage-related
  (contain `points` + `damage`) are counted and written to an `unparsed` sink so the
  grammar can be widened later — they must never crash or silently vanish.
- When adding support for a new log line form: **(1)** add a fixture line + expected
  parse to `tests/fixtures/`, **(2)** add the rule, **(3)** document the form in
  `docs/log-format.md`.
- Pet attribution: a name seen as `Name (Owner: X)` (or tied to the logging character
  via a `#petcmd` response) is a pet; its output rolls into the owner's totals as a
  `pet: <name>` sub-group. Pets with no determinable owner stay their own entity.
  Owner assignment is retroactive within a session.

## Code / workflow conventions

- Target framework: `.NET 8` (`net8.0`, and `net8.0-windows` for the WPF app).
- Run `dotnet build` and `dotnet test` before every commit; both must be green.
- Work on a branch off `main`; do not commit directly to `main`. Open a PR.
- End commit messages with:
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`
- End PR descriptions with:
  `🤖 Generated with [Claude Code](https://claude.com/claude-code)`
- Keep changes scoped; note follow-up ideas in `CHANGELOG.md`'s Unreleased section or
  as GitHub issues rather than expanding the current change.

## Cutting a release

Binaries are **never committed** — they ship as GitHub Release assets built by CI from
a version tag. To release:

1. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [x.y.z] - <date>` and add a fresh
   empty `## [Unreleased]` above it.
2. Bump `<Version>` in `Directory.Build.props` to `x.y.z` (dev-build fallback; CI passes
   the real version from the tag).
3. Merge to `main`, then from `main`: `git tag vx.y.z && git push origin vx.y.z`.
4. `.github/workflows/release.yml` runs the tests, publishes both single-file win-x64
   exes with `-p:Version=<tag>`, and creates the Release with `SHA256SUMS.txt` and
   auto-generated notes. Releases are marked pre-release while on `0.x`.

## Layout

| Path | Purpose |
|---|---|
| `src/TriuneLogParser.Core` | Parsing, model, encounter building, aggregation. No UI. |
| `src/TriuneLogParser.Cli`  | `triuneparse` console tool — parse/tail a file, dump table/JSON. Primary pre-UI verification path. |
| `src/TriuneLogParser.App`  | WPF desktop app: encounter browser + overlay. |
| `tests/TriuneLogParser.Core.Tests` | xUnit tests, fixture-driven. |
| `docs/log-format.md` | Living spec of the Triune log grammar. |
| `samples/` | Tiny sanitized log excerpts for demos/tests. |
