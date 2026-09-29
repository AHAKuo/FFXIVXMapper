namespace XMapper;

/// <summary>
/// Slot arithmetic for cross hotbars. Hotbar ids 10..17 are cross hotbar sets 1..8; each has 16 slots:
/// 0-3 L2 d-pad, 4-7 L2 face buttons, 8-11 R2 d-pad, 12-15 R2 face buttons.
/// D-pad order: up, right, down, left. Face buttons: top, right, bottom, left.
/// If this turns out to differ in game, only this file needs changing.
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

    public static (Half half, Cluster cluster, int index) Decompose(int slot)
    {
        var half = slot < 8 ? Half.L2 : Half.R2;
        var within = slot % 8;
        var cluster = within < 4 ? Cluster.Dpad : Cluster.Buttons;
        return (half, cluster, within % 4);
    }

    public static readonly string[] DpadLabels = ["Up", "Right", "Down", "Left"];
    public static readonly string[] ButtonLabelsXbox = ["Y", "B", "A", "X"];
    public static readonly string[] ButtonLabelsPlayStation = ["△", "○", "✕", "□"];

    public static string SlotLabel(Cluster cluster, int i, bool playStation) =>
        cluster == Cluster.Dpad ? DpadLabels[i] : (playStation ? ButtonLabelsPlayStation[i] : ButtonLabelsXbox[i]);
}
