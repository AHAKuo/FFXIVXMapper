namespace XMapper;

/// <summary>Position within a four-slot cluster. Values match the slot index order: left = 0, up/top = 1, right = 2, down/bottom = 3.</summary>
public enum Direction { Left = 0, Up = 1, Right = 2, Down = 3 }

/// <summary>
/// Slot arithmetic for cross hotbars. Hotbar ids 10..17 are cross hotbar sets 1..8; each has 16 slots:
/// 0-3 L2 d-pad, 4-7 L2 face buttons, 8-11 R2 d-pad, 12-15 R2 face buttons.
/// Within a group the order is left, up, right, down (d-pad) / left, top, right, bottom (face buttons), i.e. clockwise from West.
/// Half/cluster order verified in game 2026-09-29; the within-group order verified with /xmapper probe on 2026-10-06.
/// </summary>
public static class CrossHotbar
{
    public const uint FirstBarId = 10;
    public const int SetCount = 8;
    public const int SlotsPerSet = 16;
    public const int SlotsPerRegion = 4;

    public static uint BarId(int set) => FirstBarId + (uint)(set - 1);

    public static int SlotIndex(Half half, Cluster cluster, int i) =>
        (half == Half.L2 ? 0 : 8) + (cluster == Cluster.Dpad ? 0 : 4) + i;

    public static int SlotIndex(Region r, int i) => SlotIndex(r.Half, r.Cluster, i);

    /// <summary>
    /// The i-th position (0..3) within a cluster when filling clockwise from <paramref name="first"/>.
    /// Starting at Left gives left, up, right, down, which is also the game's own slot order, so the first action lands on West (□ / X / d-pad left).
    /// </summary>
    public static int FillIndex(Direction first, int i) => ((int)first + i) % SlotsPerRegion;

    /// <summary>Slot for the i-th action placed in a region, honouring the bias's fill start.</summary>
    public static int FillSlot(Region r, Direction first, int i) => SlotIndex(r.Half, r.Cluster, FillIndex(first, i));

    public static (Half half, Cluster cluster, int index) Decompose(int slot)
    {
        var half = slot < 8 ? Half.L2 : Half.R2;
        var within = slot % 8;
        var cluster = within < 4 ? Cluster.Dpad : Cluster.Buttons;
        return (half, cluster, within % 4);
    }

    public static readonly string[] DpadLabels = ["Left", "Up", "Right", "Down"];
    public static readonly string[] ButtonLabelsXbox = ["X", "Y", "B", "A"];
    public static readonly string[] ButtonLabelsPlayStation = ["□", "△", "○", "✕"];

    public static string SlotLabel(Cluster cluster, int i, bool playStation) =>
        cluster == Cluster.Dpad ? DpadLabels[i] : (playStation ? ButtonLabelsPlayStation[i] : ButtonLabelsXbox[i]);

    /// <summary>Label for a fill-start choice, e.g. "West (□ / X / d-pad left)".</summary>
    public static string DirectionLabel(Direction d, bool playStation)
    {
        var compass = d switch { Direction.Up => "North", Direction.Right => "East", Direction.Down => "South", _ => "West" };
        return $"{compass} ({SlotLabel(Cluster.Buttons, (int)d, playStation)} / d-pad {DpadLabels[(int)d].ToLowerInvariant()})";
    }
}
