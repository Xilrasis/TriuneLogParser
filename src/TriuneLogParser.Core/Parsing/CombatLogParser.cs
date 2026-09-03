using System.Text.RegularExpressions;
using TriuneLogParser.Core.Logging;
using TriuneLogParser.Core.Model;

namespace TriuneLogParser.Core.Parsing;

/// <summary>
/// Turns raw log lines into <see cref="CombatEvent"/>s using an ordered set of named
/// regex rules. First match wins. Unknown lines that look damage-related are reported
/// as <see cref="ParseOutcome.UnparsedDamageLike"/> so the grammar can be widened
/// without losing data.
/// </summary>
public sealed partial class CombatLogParser
{
    private readonly NameResolver _names;
    private readonly PetRegistry _pets;

    public CombatLogParser(NameResolver names, PetRegistry pets)
    {
        _names = names;
        _pets = pets;
    }

    public PetRegistry Pets => _pets;
    public NameResolver Names => _names;

    /// <summary>Parse one already-split log line.</summary>
    public ParseOutcome Parse(in LogLine line)
    {
        string msg = line.Message;

        // ---- strip the Triune pet "(Owner: X)" tag, if present -------------------
        string? explicitAttacker = null;
        string? ownerHint = null;
        Match tag = PetTagRegex().Match(msg);
        if (tag.Success)
        {
            explicitAttacker = tag.Groups["pet"].Value;
            ownerHint = tag.Groups["owner"].Value;
            _pets.RegisterOwnedPet(explicitAttacker, ownerHint);
            msg = tag.Groups["rest"].Value;
        }

        ParseOutcome outcome = Dispatch(line, msg, explicitAttacker, ownerHint);
        if (outcome.Handled)
            return outcome;

        // Nothing matched — flag if it still looks like combat damage.
        if (DamageLikeRegex().IsMatch(msg))
            return ParseOutcome.Unparsed();

        return ParseOutcome.None;
    }

    private ParseOutcome Dispatch(in LogLine line, string msg, string? explicitAttacker, string? ownerHint)
    {
        Match m;

        if ((m = ZoneRegex().Match(msg)).Success)
            return ParseOutcome.FromZone(new ZoneChange(line.Timestamp, m.Groups["zone"].Value));

        if ((m = SlainByRegex().Match(msg)).Success)
            return Death(line, victim: m.Groups["t"].Value, killer: m.Groups["k"].Value);

        if ((m = YouSlewRegex().Match(msg)).Success)
            return Death(line, victim: m.Groups["t"].Value, killer: "You");

        if ((m = YouWereSlainRegex().Match(msg)).Success)
            return Death(line, victim: "You", killer: m.Groups["k"].Value);

        if ((m = CritRegex().Match(msg)).Success)
        {
            bool self = NameResolver.IsSelfToken(m.Groups["who"].Value) || _names.IsSelf(_names.Resolve(m.Groups["who"].Value));
            string? sp = m.Groups["sp"].Success ? m.Groups["sp"].Value : null;
            return ParseOutcome.FromCrit(new CritMarker(line.Timestamp, ParseAmount(m), sp, self, line.Raw));
        }

        if ((m = HealYouRegex().Match(msg)).Success)
            return Heal(line, healer: null, target: "You", m);

        if ((m = HealGenericRegex().Match(msg)).Success)
            return Heal(line, healer: explicitAttacker ?? m.Groups["h"].Value, target: m.Groups["t"].Value, m, ownerHint);

        if (explicitAttacker != null && (m = StrippedHealRegex().Match(msg)).Success)
            return Heal(line, healer: explicitAttacker, target: m.Groups["t"].Value, m, ownerHint);

        if ((m = HealYouCastRegex().Match(msg)).Success)
            return Heal(line, healer: "You", target: m.Groups["t"].Value, m);

        if ((m = DotFromByRegex().Match(msg)).Success)
            return Damage(line, attacker: m.Groups["caster"].Value, target: m.Groups["t"].Value,
                amount: ParseAmount(m), DamageMechanic.NonMelee, verb: null, spell: m.Groups["sp"].Value, ownerHint);

        if ((m = DotFromYourRegex().Match(msg)).Success)
            return Damage(line, attacker: "You", target: m.Groups["t"].Value,
                amount: ParseAmount(m), DamageMechanic.NonMelee, verb: null, spell: m.Groups["sp"].Value, ownerHint);

        if ((m = DamageShieldRegex().Match(msg)).Success)
            return Damage(line, attacker: explicitAttacker, target: m.Groups["t"].Value,
                amount: ParseAmount(m), DamageMechanic.DamageShield, verb: null, spell: null, ownerHint);

        if ((m = NonMeleeAttackerRegex().Match(msg)).Success)
            return Damage(line, attacker: explicitAttacker ?? m.Groups["a"].Value, target: m.Groups["t"].Value,
                amount: ParseAmount(m), DamageMechanic.NonMelee, verb: null,
                spell: m.Groups["sp"].Success ? m.Groups["sp"].Value : null, ownerHint);

        if ((m = NonMeleeNoAttackerRegex().Match(msg)).Success)
            return Damage(line, attacker: explicitAttacker, target: m.Groups["t"].Value,
                amount: ParseAmount(m), DamageMechanic.NonMelee, verb: null,
                spell: m.Groups["sp"].Success ? m.Groups["sp"].Value : null, ownerHint);

        if ((m = SelfMeleeHitRegex().Match(msg)).Success)
        {
            string verb = Verbs.Normalize(m.Groups["verb"].Value);
            return Damage(line, attacker: "You", target: m.Groups["t"].Value, amount: ParseAmount(m),
                Verbs.Classify(verb), verb, spell: m.Groups["sp"].Success ? m.Groups["sp"].Value : null, ownerHint);
        }

        if ((m = SelfMeleeMissRegex().Match(msg)).Success)
            return MissOutcome(line, attacker: "You", target: m.Groups["t"].Value,
                Verbs.Normalize(m.Groups["verb"].Value), m.Groups["reason"].Value);

        if ((m = IncomingMeleeRegex().Match(msg)).Success)
        {
            string verb = Verbs.Normalize(m.Groups["verb"].Value);
            return Damage(line, attacker: m.Groups["a"].Value, target: "You", amount: ParseAmount(m),
                Verbs.Classify(verb), verb, spell: m.Groups["sp"].Success ? m.Groups["sp"].Value : null, ownerHint);
        }

        if ((m = OtherMeleeHitRegex().Match(msg)).Success)
        {
            string verb = Verbs.Normalize(m.Groups["verb"].Value);
            return Damage(line, attacker: explicitAttacker ?? m.Groups["a"].Value, target: m.Groups["t"].Value,
                amount: ParseAmount(m), Verbs.Classify(verb), verb,
                spell: m.Groups["sp"].Success ? m.Groups["sp"].Value : null, ownerHint);
        }

        if ((m = StrippedMeleeHitRegex().Match(msg)).Success)
        {
            string verb = Verbs.Normalize(m.Groups["verb"].Value);
            return Damage(line, attacker: explicitAttacker, target: m.Groups["t"].Value,
                amount: ParseAmount(m), Verbs.Classify(verb), verb, spell: null, ownerHint);
        }

        if ((m = OtherMeleeMissRegex().Match(msg)).Success)
            return MissOutcome(line, attacker: m.Groups["a"].Value, target: m.Groups["t"].Value,
                Verbs.Normalize(m.Groups["verb"].Value), m.Groups["reason"].Value);

        return ParseOutcome.None;
    }

    // ---- outcome builders ---------------------------------------------------------

    private ParseOutcome Damage(in LogLine line, string? attacker, string target, long amount,
        DamageMechanic mechanic, string? verb, string? spell, string? ownerHint)
    {
        string? resolvedAttacker = attacker is null ? null : _names.Resolve(attacker);
        string resolvedTarget = _names.Resolve(target);
        string? owner = ResolveOwner(resolvedAttacker, ownerHint);

        return ParseOutcome.FromEvent(new CombatEvent
        {
            Timestamp = line.Timestamp,
            Action = CombatAction.Damage,
            Attacker = resolvedAttacker,
            Target = resolvedTarget,
            AttackerOwner = owner,
            Amount = amount,
            Mechanic = mechanic,
            Verb = verb,
            SpellName = NormalizeSpell(spell),
            LineNumber = line.LineNumber,
            RawLine = line.Raw,
        });
    }

    private ParseOutcome MissOutcome(in LogLine line, string attacker, string target, string verb, string reason)
    {
        return ParseOutcome.FromEvent(new CombatEvent
        {
            Timestamp = line.Timestamp,
            Action = CombatAction.Miss,
            Attacker = _names.Resolve(attacker),
            Target = _names.Resolve(target),
            Mechanic = Verbs.Classify(verb),
            Verb = verb,
            MissReason = ClassifyMiss(reason),
            LineNumber = line.LineNumber,
            RawLine = line.Raw,
        });
    }

    private ParseOutcome Heal(in LogLine line, string? healer, string target, Match m, string? ownerHint = null)
    {
        string resolvedTarget = _names.Resolve(target);
        if (resolvedTarget.Equals("itself", StringComparison.OrdinalIgnoreCase) ||
            NameResolver.IsSelfToken(target) && healer != null)
        {
            resolvedTarget = _names.Resolve(healer ?? target);
        }

        string? resolvedHealer = healer is null ? null : _names.Resolve(healer);
        return ParseOutcome.FromEvent(new CombatEvent
        {
            Timestamp = line.Timestamp,
            Action = CombatAction.Heal,
            Attacker = resolvedHealer,
            Target = resolvedTarget,
            AttackerOwner = ResolveOwner(resolvedHealer, ownerHint),
            Amount = ParseAmount(m),
            SpellName = m.Groups["sp"].Success ? NormalizeSpell(m.Groups["sp"].Value) : null,
            LineNumber = line.LineNumber,
            RawLine = line.Raw,
        });
    }

    private ParseOutcome Death(in LogLine line, string victim, string killer)
    {
        return ParseOutcome.FromEvent(new CombatEvent
        {
            Timestamp = line.Timestamp,
            Action = CombatAction.Death,
            Attacker = _names.Resolve(killer),
            Target = _names.Resolve(victim),
            LineNumber = line.LineNumber,
            RawLine = line.Raw,
        });
    }

    private string? ResolveOwner(string? attacker, string? ownerHint)
    {
        if (attacker is null)
            return null;
        if (!string.IsNullOrEmpty(ownerHint))
            return _names.Resolve(ownerHint);
        return _pets.OwnerOf(attacker);
    }

    private static long ParseAmount(Match m) =>
        m.Groups["amt"].Success && long.TryParse(m.Groups["amt"].Value, out long v) ? v : 0;

    private static string? NormalizeSpell(string? spell)
    {
        if (string.IsNullOrWhiteSpace(spell))
            return null;
        spell = spell.Trim();
        // Non-damage flavour sources we don't want polluting the breakdown as "spells".
        return spell;
    }

    private static string ClassifyMiss(string reason)
    {
        reason = reason.ToLowerInvariant();
        if (reason.Contains("miss")) return "miss";
        if (reason.Contains("parr")) return "parry";
        if (reason.Contains("dodge")) return "dodge";
        if (reason.Contains("block")) return "block";
        if (reason.Contains("riposte")) return "riposte";
        if (reason.Contains("invulnerable")) return "invulnerable";
        if (reason.Contains("rune") || reason.Contains("absorb")) return "rune";
        if (reason.Contains("magical skin")) return "rune";
        return "miss";
    }

    // ---- rules (ordered; see docs/log-format.md) --------------------------------

    [GeneratedRegex(@"^(?<pet>[A-Z][A-Za-z`'-]+) \(Owner: (?<owner>[A-Z][A-Za-z`'-]+)\) (?<rest>.+)$")]
    private static partial Regex PetTagRegex();

    [GeneratedRegex(@"points? of (?:non-melee )?damage|has taken \d+ damage", RegexOptions.IgnoreCase)]
    private static partial Regex DamageLikeRegex();

    [GeneratedRegex(@"^You have entered (?<zone>.+?)\.$")]
    private static partial Regex ZoneRegex();

    [GeneratedRegex(@"^(?<t>.+?) has been slain by (?<k>.+?)!$")]
    private static partial Regex SlainByRegex();

    [GeneratedRegex(@"^You have slain (?<t>.+?)!$")]
    private static partial Regex YouSlewRegex();

    [GeneratedRegex(@"^You have been slain by (?<k>.+?)!$")]
    private static partial Regex YouWereSlainRegex();

    [GeneratedRegex(@"^(?<who>.+?) (?:scores? a critical hit|lands? a Crippling Blow|delivers? a critical blast)! \((?<amt>\d+)\)(?: \((?<sp>.+)\))?$")]
    private static partial Regex CritRegex();

    [GeneratedRegex(@"^You have been healed for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex HealYouRegex();

    [GeneratedRegex(@"^(?<h>.+?) (?:has|have) healed (?<t>.+?) for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex HealGenericRegex();

    [GeneratedRegex(@"^You heal (?<t>.+?) for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex HealYouCastRegex();

    [GeneratedRegex(@"^(?:has|have) healed (?<t>.+?) for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex StrippedHealRegex();

    [GeneratedRegex(@"^(?<t>.+?) has taken (?<amt>\d+) damage from (?<sp>.+?) by (?<caster>.+?)\.$")]
    private static partial Regex DotFromByRegex();

    [GeneratedRegex(@"^(?<t>.+?) has taken (?<amt>\d+) damage from your (?<sp>.+?)\.$")]
    private static partial Regex DotFromYourRegex();

    [GeneratedRegex(@"^(?<t>.+?) was hit by non-melee for (?<amt>\d+) points of damage\.$")]
    private static partial Regex DamageShieldRegex();

    [GeneratedRegex(@"^(?<a>.+?) hit (?<t>.+?) for (?<amt>\d+) points of non-melee damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex NonMeleeAttackerRegex();

    [GeneratedRegex(@"^hit (?<t>.+?) for (?<amt>\d+) points of non-melee damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex NonMeleeNoAttackerRegex();

    [GeneratedRegex(@"^You (?<verb>[a-z]+) (?<t>.+?) for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex SelfMeleeHitRegex();

    [GeneratedRegex(@"^You try to (?<verb>[a-z]+) (?<t>.+?), but (?<reason>[^!]+)!$")]
    private static partial Regex SelfMeleeMissRegex();

    [GeneratedRegex(@"^(?<a>.+?) (?<verb>[a-z]+) YOU for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex IncomingMeleeRegex();

    [GeneratedRegex(@"^(?<a>.+?) (?<verb>hits|crushes|slashes|pierces|bites|claws|gores|stings|mauls|smashes|slams|rends|burns|freezes|slices|kicks|punches|bashes|backstabs|strikes|frenzies on) (?<t>.+?) for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex OtherMeleeHitRegex();

    [GeneratedRegex(@"^(?<verb>hits|crushes|slashes|pierces|bites|claws|gores|stings|mauls|smashes|slams|rends|burns|freezes|slices|kicks|punches|bashes|backstabs|strikes|frenzies on) (?<t>.+?) for (?<amt>\d+) points of damage\.(?: \((?<sp>.+)\))?$")]
    private static partial Regex StrippedMeleeHitRegex();

    [GeneratedRegex(@"^(?<a>.+?) tries to (?<verb>[a-z]+) (?<t>.+?), but (?<reason>[^!]+)!$")]
    private static partial Regex OtherMeleeMissRegex();
}
