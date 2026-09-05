# TriuneLogParser

A real-time EverQuest combat-log parser and damage meter for the
**[Project Triune](https://www.projecttriune.com/)** server — a multiclass server
running the RoF2 client whose log wording is different enough that mainstream parsers
(EQLogParser, EQ Legends Companion) don't fully work with it.

> **Status:** early development, but all four planned phases have landed — the parsing
> engine, the `triuneparse` CLI, the WPF desktop app (live encounter browser +
> damage-meter breakdown), the always-on-top overlay, and class inference / export /
> per-mob views. See [the roadmap](#roadmap) and [CHANGELOG.md](CHANGELOG.md).

## Features

- **Live parsing.** Point it at your EverQuest folder; it tails
  `logs/eqlog_<Character>_<server>.txt` as the game writes it.
- **Damage breakdown by source.** Per player: melee by swing type (crush, pierce,
  kick, punch, backstab, strike…), direct non-melee (nukes), damage-over-time,
  procs, and damage shields — kept as separate buckets, not one lump number.
- **Damage taken, healing, deaths** tracked alongside damage done.
- **Pet attribution.** Pet output is credited to its owner (detected from Triune's
  `Name (Owner: X)` log tags) as a labelled sub-group; unowned pets stay separate.
- **Encounter splitting.** EQLogParser-style fight detection that groups multi-mob
  pulls into one encounter and closes fights on death or inactivity, with a
  configurable rest period and raid-aware handling of deaths and corpse runs.
- **Force split.** When detection gets a boundary wrong, a button or **Ctrl+Alt+S**
  ends the current encounter on the spot. The split is saved beside the log, so
  re-parsing reproduces it.
- **Time-range grouping.** Merge any set of encounters into one aggregate view.
- **Always-on-top overlay** — draggable damage-meter bars pinned over the game, with a
  click-through mode, a one-click chat-ready summary, and (experimental) a click-through
  to any player's per-source breakdown.

## Requirements

- Windows 10/11 (x64). The released app is a single self-contained `.exe` — no .NET
  install required.
- To build from source: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Getting started (from source)

```bash
git clone https://github.com/<owner>/TriuneLogParser.git
cd TriuneLogParser
dotnet build
dotnet test
```

### CLI (`triuneparse`)

Parse a log file and print an encounter-by-encounter breakdown:

```bash
dotnet run --project src/TriuneLogParser.Cli -- "C:\EverQuest\logs\eqlog_Yourname_multiclass.txt" --table
```

Follow a live log (updates as the game writes):

```bash
dotnet run --project src/TriuneLogParser.Cli -- "C:\EverQuest\logs\eqlog_Yourname_multiclass.txt" --follow
```

`--json` emits machine-readable output instead of the table. `--rest <seconds>` picks
the encounter model (`0` = one encounter per pull, `>0` = session/event style).
Saved [split markers](docs/log-format.md) are applied automatically; `--no-markers`
ignores them.

### Building the standalone app

```bash
dotnet publish src/TriuneLogParser.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The resulting `TriuneLogParser.exe` under `src/TriuneLogParser.App/bin/Release/net8.0-windows/win-x64/publish/`
is fully portable. Tagged releases attach this build to the
[GitHub Releases](../../releases) page automatically.

## How it works

The [`docs/log-format.md`](docs/log-format.md) file documents every log line form the
parser recognizes, with real examples. Parsing is rule-driven: each rule is a named
regex that converts one line into a typed combat event. Unknown but damage-shaped
lines are captured for later rather than dropped, so the parser degrades gracefully on
content it hasn't seen.

## Roadmap

| Phase | Scope |
|---|---|
| 1 | ✅ Parsing engine, encounter builder, aggregation, `triuneparse` CLI |
| 2 | ✅ WPF app: first-run EQ-folder picker, live tail, encounter browser, breakdown tree |
| 3 | ✅ Always-on-top overlay with configurable damage-meter bars |
| 4 | ✅ Class inference, encounter export, per-mob views  ·  _(session history: later)_ |

See [CHANGELOG.md](CHANGELOG.md) for what has landed.

## Contributing

Read [AGENTS.md](AGENTS.md) first — it applies to human and AI contributors alike.
In short: keep `Core` UI-free, add a fixture before a parser rule, update the README
and CHANGELOG with user-facing changes, never commit real player logs.

## License

[MIT](LICENSE). Not affiliated with Daybreak Game Company or Project Triune.
