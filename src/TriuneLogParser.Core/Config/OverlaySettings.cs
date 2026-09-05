namespace TriuneLogParser.Core.Config;

/// <summary>Which number the overlay bars rank and display.</summary>
public enum OverlayMetric
{
    Dps = 0,
    Damage,
    DamagePlusHealing,
    DamageTaken,
    Healing,
}

/// <summary>Persisted preferences for the always-on-top overlay window.</summary>
public sealed class OverlaySettings
{
    public bool Shown { get; set; }

    public double Left { get; set; } = 40;
    public double Top { get; set; } = 40;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 260;

    /// <summary>Background opacity, 0.05–1.0.</summary>
    public double Opacity { get; set; } = 0.82;

    /// <summary>UI scale for the overlay, 0.7–2.0.</summary>
    public double Scale { get; set; } = 1.0;

    public int MaxRows { get; set; } = 8;

    public OverlayMetric Metric { get; set; } = OverlayMetric.Dps;

    /// <summary>Pets folded into their owner's bar (vs. shown as their own bar).</summary>
    public bool FoldPets { get; set; } = true;

    /// <summary>Mouse events pass through to the game behind the overlay.</summary>
    public bool ClickThrough { get; set; }

    /// <summary>Position and size are pinned — no drag-move, no resize.</summary>
    public bool Locked { get; set; }

    /// <summary>Show the encounter title / duration header.</summary>
    public bool ShowHeader { get; set; } = true;

    // ---- experimental "branch" overlay (per-player source breakdown) ----
    public double BranchLeft { get; set; } = 380;
    public double BranchTop { get; set; } = 40;
    public double BranchWidth { get; set; } = 320;
    public double BranchHeight { get; set; } = 240;

    public OverlaySettings Clamp()
    {
        Opacity = Math.Clamp(Opacity, 0.05, 1.0);
        Scale = Math.Clamp(Scale, 0.7, 2.0);
        MaxRows = Math.Clamp(MaxRows, 1, 30);
        Width = Math.Clamp(Width, 160, 1600);
        Height = Math.Clamp(Height, 90, 1600);
        BranchWidth = Math.Clamp(BranchWidth, 160, 1600);
        BranchHeight = Math.Clamp(BranchHeight, 90, 1600);
        return this;
    }
}
