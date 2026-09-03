# Changelog

All notable changes to TriuneLogParser are recorded here. Entries are high-level and
feature-scoped; newest first. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/). This project uses
[Semantic Versioning](https://semver.org/) (0.x = pre-release, expect churn).

## [Unreleased]

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
- `triuneparse` CLI: parse a log file or follow it live, output a summary table or
  JSON.
