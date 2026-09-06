# Changelog

All notable changes to TriuneLogParser are recorded here. Entries are high-level and
feature-scoped; newest first. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/). This project uses
[Semantic Versioning](https://semver.org/) (0.x = pre-release, expect churn).

## [Unreleased]

### Added
- **Defenses view.** A new breakdown mode next to Damage Done / Taken / Healing / Mobs.
  For each defender it lists incoming attacks by type (melee verb, or `non-melee:
  <spell>`) with min / average / max hit, attacker hit-rate, and miss / parry / dodge /
  block / riposte / rune counts and rates — expand a type row for the full split. Works
  for the whole group, not just the logging character. Also in the CSV/JSON export and
  the `triuneparse --table` output.
- **Resizable overlay columns.** Both overlays now have a column header row (Name /
  Total / Rate) with thin drag handles between the columns, Excel-style. Columns stay
  proportional and rescale with the window by default; a drag changes the ratio and
  persists it. The handles hide when the overlay is locked.
- The Mobs view is now a full tree: expand a mob to see the players that damaged it,
  then expand a player for their per-source breakdown — the same melee-collapse /
  pet-fold / skill-attack layout as Damage Done.
- The app version is shown in the main window title bar.

### Fixed
- Overlay tooltips were light text on the OS default light background — unreadable.
  They now use the app's dark theme.
- The ◀ / ▶ metric buttons in the overlay settings panel were invisible — the default
  button padding clipped the glyph and the fill matched the panel. They're now visible
  stepper buttons.

## [0.3.0] - 2026-09-05

### Added
- **Copy parse summary.** A clipboard button (📋) on the overlay builds a one-line,
  plain-ASCII summary of the bars currently shown — ranked fighters with value, share
  and rate, plus a raid total — sized to paste straight into an EverQuest chat channel.
  Truncates by dropping the lowest contributors first if it would run long.
- **Player breakdown in the overlay (experimental).** Click a bar in the overlay to
  open a second "branch" overlay — same style — showing that player's individual
  damage sources as bars, live. It's a separate window so it can be iterated on without
  touching the main overlay.
- **Clear encounter history.** A button on the main window drops all parsed encounters
  and keeps following the log from now — a reload with no history lookback.
- **Split the log now.** A button in Settings archives the active log immediately
  (the manual form of the size trigger); the game starts a fresh log on its next write.
- Grammar misses are appended to `%AppData%/TriuneLogParser/unparsed.log` — one real
  example of each distinct unparsed line shape, for filling in missing rules.

### Changed
- The default rest period is now **0** (one encounter per pull). Existing settings are
  unchanged.
- Both overlays are now drag-resizable (a handle in the bottom-right corner) and
  remember their size as well as their position. They cap at the screen and scroll the
  bar list past that, with a thin (7px), auto-dimming scrollbar — a high row count no
  longer runs the window off-screen.
- Overlay bar columns are proportional now — they shrink and grow with the window
  instead of overflowing the right edge when it's narrowed.
- A **lock** toggle (🔒) in the overlay titlebar pins position and size — no drag-move,
  no resize — for the overlay and its player-breakdown popup. Persisted.

### Fixed
- Pet-owned rune / absorb lines (`<pet> (Owner: X) has shielded itself from N points of
  damage.`) were counted as grammar misses — the `(Owner: X)` strip removes the caster,
  so the rule no longer required one.

## [0.2.0] - 2026-09-04

### Added
- **WPF desktop app (Phase 2).** First-run prompt for the EverQuest folder (persisted
  to `%AppData%/TriuneLogParser/settings.json`), automatic discovery of character log
  files, and a live tail that bulk-loads the existing log then follows new lines.
- **Encounter browser.** Compact chronological list of encounters; select one — or
  Ctrl/Shift-click several — to see a merged breakdown.
- **Damage-meter breakdown.** Full-height proportional bars per fighter with the
  numbers overlaid, expandable into a source tree: auto-attack swings collapse into a
  single "Melee" line, each pet collapses into one line, and skill attacks
  (kick, strike, backstab, frenzy, bash, punch) and spells stay separate — every row
  shows its share of the parent. Damage-done / damage-taken / healing views.
- **Always-on-top overlay (Phase 3).** Borderless, draggable, resizable damage-meter
  window that stays above the game. Ranked bars for the live fight (falling back to the
  last one), with metric selection (DPS / damage / damage+heals / damage taken /
  healing), adjustable opacity, UI scale and row count, and a click-through mode
  toggled from the main window. Position and preferences persist.
- **Per-mob view, class inference and export (Phase 4).**
  - A **Mobs** view: damage taken per NPC, time-to-kill, killing blow, and which
    fighters contributed.
  - **Class inference** — signature abilities/spells map each player to its class(es)
    (multiclass-aware, e.g. "Monk / Enchanter / Necromancer"), shown on the fighter row.
  - **Export** a selected encounter (or range) to CSV or JSON.
  - `triuneparse --table` prints a per-mob summary; `triuneparse --rest <seconds>`
    selects the rest-period model (0 = per-pull, >0 = session/event).
- **Settings** dialog: configurable **rest period between fights** (0–5 min; 0 = one
  encounter per pull), **retroactive parse depth** on start (active-only / 30 min / 1 /
  2 / 6 / 24 h), and an optional **log auto-archive** that renames the log aside with a
  timestamp once it passes a size (default 200 MB, off).
- **Force encounter split.** A "Split fight" button (main window and overlay) and a
  global **Ctrl+Alt+S** hotkey end the encounter in progress immediately; the next
  combat line starts a fresh one, with the re-engage and corpse-run merges suppressed
  so the boundary lands exactly where asked. The split is saved as a marker beside the
  log (`%AppData%/TriuneLogParser/markers/<character>.json`), keyed to the character so
  it survives log archiving — re-parsing (retro parse, restart, `triuneparse`)
  reproduces the same boundaries. `triuneparse --markers <file>` / `--no-markers`
  control it from the CLI.
- Overlay rows show *name · total (%) · DPS*, larger and shadowed for contrast, muted
  bars, window auto-sizes to the row count. Breakdown rows match: indented sub-entries,
  bold expand arrows, a narrow proportional bar.

### Fixed
- Swarm / temporary pets (`Player`s Animated Corpse hits …`) are parsed and credited to
  the owning player instead of dropped as unknown NPCs. Damage the pet takes folds into
  the owner too, and a swarm pet expiring no longer counts as a player death.
- Breakdown percentage bars were always ~50% wide — they now reflect each row's share.
- Collapsing a breakdown row no longer snaps back open on the next refresh.
- Breakdown ability/entity names were near-black on the dark rows; now readable.
- A dead entity's lingering DoT ticks (`Name's corpse hit …`, and the mangled
  `Namescorpse` form) are credited to the underlying player or mob instead of spawning
  a phantom `Namescorpse` combatant.
- Grammar coverage: damage-absorb / rune / Spellshield lines and `You have taken N
  points of damage` are now recognised (previously counted as misses), and every damage
  rule accepts the singular "point of damage". Both reference logs reach 100% coverage.
- Names with trailing or doubled spaces (`Zebuxoruk `, `Emperor  Ssraeshza`) are
  normalised so one entity isn't split in two.
- Raid encounters no longer fragment on every death: after the logging character dies,
  the fight is held open through the corpse run (idle gap + release-to-bind zoning +
  run back) for up to 3 minutes so a phased event stays one encounter. `"an Instanced
  Version of the zone"` is no longer treated as a zone change.
- A one-word-named raid boss that kills players (`<player> has been slain by <Boss>!`)
  is no longer misclassified as a player — its damage was being dropped as friendly
  fire and it never appeared as a mob. Hard evidence (a known player hit it) now
  outranks the name/kill heuristics; on the reference raid log this collapses the
  Zebuxoruk event from five encounters (boss missing) to one or two.
- Rest period 0 splits more cleanly — a mob that only swung at you once no longer holds
  the per-pull encounter open until it dies.

### Known issues
- Numbers in the breakdown are left-aligned in fixed columns rather than right-aligned
  (a `TextAlignment="Right"` rendering bug on some Windows 11 builds).

## [0.1.0] - 2026-09-03

### Added
- Project scaffold: solution, `Core` parsing library, `triuneparse` CLI, xUnit test
  project, CI + release GitHub Actions workflows.
- Rule-driven log parser for the Project Triune log grammar: self/other melee hits and
  misses, non-melee direct damage and DoT ticks, spell/proc sources, damage shields,
  incoming damage, heals, and death lines. Unknown damage-like lines are captured
  rather than dropped.
- Pet attribution: pets are identified from `Name (Owner: X)` tags (and pet-command
  responses) and their output is credited to the owner as a `pet: <name>` sub-group;
  ownerless pets remain separate entities.
- Encounter builder: EQLogParser-style fight splitting with multi-mob pull grouping,
  configurable idle timeout, and death/zone fight-end detection.
- Aggregation: per-player, per-source damage / damage-taken / healing rollups, with
  time-range grouping across multiple encounters.
- `triuneparse` CLI: parse a log file or follow it live, output a summary table or JSON.
