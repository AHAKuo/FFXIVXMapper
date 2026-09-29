using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace XMapper;

/// <summary>Writes a plan to the game's cross hotbars through the same call the game uses when you drag an action.</summary>
public static unsafe class HotbarWriter
{
    public sealed record Result(int Cleared, int Written, string? Error);

    public static Result Apply(Plan plan)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return new Result(0, 0, "Hotbar module is not available.");

        var cleared = 0;
        var bias = plan.Bias;

        if (bias.ClearWholeSets)
        {
            foreach (var set in plan.UsedSets())
            {
                for (var slot = 0; slot < CrossHotbar.SlotsPerSet; slot++)
                {
                    module->SetAndSaveSlot(CrossHotbar.BarId(set), (uint)slot, RaptureHotbarModule.HotbarSlotType.Empty, 0);
                    cleared++;
                }
            }
        }
        else if (bias.ClearOwnedRegions)
        {
            foreach (var region in bias.OwnedRegions())
            {
                for (var i = 0; i < CrossHotbar.SlotsPerRegion; i++)
                {
                    var slot = CrossHotbar.SlotIndex(region, i);
                    module->SetAndSaveSlot(CrossHotbar.BarId(region.Set), (uint)slot, RaptureHotbarModule.HotbarSlotType.Empty, 0);
                    cleared++;
                }
            }
        }

        var written = 0;
        foreach (var p in plan.Placements)
        {
            module->SetAndSaveSlot(p.BarId, (uint)p.Slot, p.Action.SlotType, p.Action.Id);
            written++;
        }

        return new Result(cleared, written, null);
    }

    /// <summary>What currently sits in a cross hotbar slot, for the preview's "kept" cells.</summary>
    public static (RaptureHotbarModule.HotbarSlotType type, uint id) Peek(int set, int slot)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null) return (RaptureHotbarModule.HotbarSlotType.Empty, 0);
        var s = module->GetSlotById(CrossHotbar.BarId(set), (uint)slot);
        return s == null ? (RaptureHotbarModule.HotbarSlotType.Empty, 0) : (s->CommandType, s->CommandId);
    }
}
