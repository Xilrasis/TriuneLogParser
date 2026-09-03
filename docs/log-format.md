# Project Triune combat-log grammar

This is the authoritative reference for every log line form TriuneLogParser
recognizes. When you teach the parser a new line, document it here in the same change.
Rules live in [`src/TriuneLogParser.Core/Parsing/CombatLogParser.cs`](../src/TriuneLogParser.Core/Parsing/CombatLogParser.cs)
and are tried in the order below (first match wins).

All lines are prefixed with an RoF2 timestamp:

```
[Www Mmm DD HH:MM:SS YYYY] <message>
```

parsed by [`LogLine`](../src/TriuneLogParser.Core/Logging/LogLine.cs). The message is
what the rules below match against.

## Identity

- The logging character comes from the file name: `eqlog_<Character>_<server>.txt`.
- `You`, `YOU`, `your`, `yourself`, `himself`, `herself` resolve to that character.
- `itself` (pet self-heal) resolves to the pet.
- A leading sentence-case article is normalized: `A doomfire soldier` → `a doomfire
  soldier`, so the mob is one entity wherever it appears in a sentence.

## Pet ownership — the main Triune difference

Project Triune tags pet actions with the owner:

```
Nixalir (Owner: Gnomies) hit a doomfire protector for 969 points of non-melee damage. (Hand of Retribution)
Karnalath (Owner: Gnomies) has healed Xilaria for 516 points of damage. (Hand of Retribution Recourse)
```

The `Name (Owner: X)` prefix is stripped before rule matching; `Name` becomes the
attacker and `X` the owner. **Melee swings from a pet are usually *untagged***
(`Nixalir crushes a doomfire guardian for 315 points of damage.`); once a name has
been seen with an `(Owner:)` tag it is treated as a pet and its untagged lines are
attributed to the owner too (retroactively within a session — see
[`PetRegistry`](../src/TriuneLogParser.Core/Parsing/PetRegistry.cs)).

Pets with no owner ever established stay their own entity.

**Swarm / temporary pets** are logged with a backtick possessive and no `(Owner:)`
tag:

```
Gnomies`s Animated Corpse hits a magma rocklord for 392 points of damage.
Gnomies`s Host of the Elements hits a doomfire soldier for 59 points of damage.
Gnomies`s Servant of Ro hits a doomfire soldier for 2 points of damage.
```

`{Player}`s {swarm type} <verb> {target} for N points of [non-melee ]damage.` — the
type becomes a pet owned by `{Player}` and its damage folds into that player as a
`pet` sub-group. Swarm pets "dying" (expiry) is not counted as a player death.

## Recognized lines

### Zone
| Form | Notes |
|---|---|
| `You have entered <zone>.` | Ends any open fight. |

### Deaths / fight end
| Form | Attacker | Target |
|---|---|---|
| `You have slain <mob>!` | character | `<mob>` |
| `<mob> has been slain by <killer>!` | `<killer>` | `<mob>` |
| `You have been slain by <mob>!` | `<mob>` | character |

### Critical markers (own line — associated with a nearby equal-amount hit)
| Form |
|---|
| `<who> scores a critical hit! (<amount>)` |
| `<who> lands a Crippling Blow! (<amount>)` |
| `<who> delivers a critical blast! (<amount>) [(<spell>)]` |

The hit may appear on the line before or after the marker. See
[`CriticalAssociator`](../src/TriuneLogParser.Core/Parsing/CriticalAssociator.cs).

### Healing
| Form |
|---|
| `You have been healed for <amount> points of damage. [(<spell>)]` |
| `<healer> has healed <target> for <amount> points of damage. [(<spell>)]` |
| `You heal <target> for <amount> points of damage. [(<spell>)]` |
| `has healed <target> for <amount> points of damage. [(<spell>)]` (pet-tag stripped) |

### Damage over time (retail wording — accepted, not seen in Triune samples yet)
| Form |
|---|
| `<target> has taken <amount> damage from <spell> by <caster>.` |
| `<target> has taken <amount> damage from your <spell>.` |

### Damage shield / environment
| Form | Mechanic |
|---|---|
| `<target> was hit by non-melee for <amount> points of damage.` | Damage Shield |

Flavor-only DS lines with no number (`... is struck by an unseen enemy.`,
`... was pierced by thorns.`) are ignored — nothing to attribute.

### Non-melee damage (nukes, DoT ticks, procs, discs)
| Form | Source |
|---|---|
| `<attacker> hit <target> for <amount> points of non-melee damage. [(<spell>)]` | `<spell>` |
| `hit <target> for <amount> points of non-melee damage. [(<spell>)]` (pet-tag stripped) | `<spell>` |

Fine-grained "DoT vs. direct nuke vs. proc" needs a spell catalogue and is deferred;
for now these are grouped under **Non-melee** by spell name.

### Melee
| Form | Mechanic |
|---|---|
| `You <verb> <target> for <amount> points of damage. [(<proc>)]` | Melee / Melee Special by verb |
| `You try to <verb> <target>, but <reason>!` | miss (reason → miss/parry/dodge/block/riposte/invulnerable/rune) |
| `<attacker> <verb-3p> <target> for <amount> points of damage. [(<proc>)]` | Melee / Melee Special |
| `<attacker> tries to <verb> <target>, but <reason>!` | miss |
| `<attacker> <verb> YOU for <amount> points of damage. [(<proc>)]` | incoming |

**Verb classification** ([`Verbs`](../src/TriuneLogParser.Core/Parsing/Verbs.cs)):
`kick`, `punch`, `bash`, `backstab`, `frenzy` and any multi-word skill →
**Melee Special**; `hit`, `crush`, `slash`, `pierce`, `strike`, `bite`, `claw`,
`gore`, `sting`, `maul`, `smash`, `slam`, `rend`, `burn`, `slice` → **Melee**.
Third-person forms (`crushes` → `crush`) are normalized.

## Deliberately ignored

AA-cap spam, `[NMS]` loot spam, killing-spree / RAMPAGE / FLURRY flavor lines (the
real damage is on its own line), MOTD, `Logging to ... is now *ON*`, absorb / rune
"shielded itself from" lines.

## Class inference

`src/TriuneLogParser.Core/Classes/ClassCatalog.cs` maps signature ability / spell
names (as they appear in the parenthetical of a non-melee hit, or as a melee verb) to
the class that uses them. Project Triune is multiclass, so `ClassTracker` reports every
class a player has shown evidence for (needs ≥2 uses of a signature). Add confirmed
signatures to the catalog as they're seen — err on the side of leaving ambiguous ones
out.

## Coverage

The parser tracks how many timestamped lines looked damage-related but matched no rule
(`triuneparse <log> --unparsed`). Against the two reference logs (~160k and ~140k
lines) coverage is 100%. Any regression here is a bug — add a rule.
