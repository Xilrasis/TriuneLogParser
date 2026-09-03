# Changelog

All notable changes to TriuneLogParser are recorded here. Entries are high-level and
feature-scoped; newest first. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/). This project uses
[Semantic Versioning](https://semver.org/) (0.x = pre-release, expect churn).

## [Unreleased]

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
- Encounter splitting reworked: per-pull by default with a short re-engage window that
  merges rapid chain-pulls, a 10-minute cap on non-stop grinds, and a session mode.
- **Always-on-top overlay (Phase 3).** Borderless, draggable, resizable damage-meter
  window that stays above the game. Shows ranked bars for the live fight (falling back
  to the last one), with metric selection (DPS / damage / damage+heals / damage taken /
  healing), adjustable opacity, UI scale and row count, and a click-through mode
  toggled from the main window. Position and preferences persist.

### Known issues
- Numbers in the breakdown are left-aligned in fixed columns rather than right-aligned
  (a `TextAlignment="Right"` rendering bug on some Windows 11 builds).
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
- `triuneparse` CLI: parse a log file or follow it live, output a summary table or
  JSON.
