using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace XMapper;

/// <summary>Writes a plan to the game's cross hotbars through the same call the game uses when you drag an action.</summary>
public static unsafe class HotbarWriter
{
    public sealed record Result(int Cleared, int Written, string? Error, int Verified = 0, string Diagnostics = "");

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

        // Stage 1: did the writes land in the live hotbar memory at all?
        var afterWrite = Verify(module, plan);

        // Stage 2: the cross hotbar display does not pick up slot writes on its own; reloading each set
        // from the saved data (which SetAndSaveSlot just updated) refreshes it.
        foreach (var set in plan.UsedSets())
            module->LoadSavedHotbar(plan.Job.RowId, CrossHotbar.BarId(set));
        var afterReload = Verify(module, plan);

        // If the job-specific reload threw our writes away, the set is probably shared: reload the shared copy.
        var afterSharedReload = -1;
        if (afterReload < afterWrite)
        {
            foreach (var set in plan.UsedSets())
                module->LoadSavedHotbar(0, CrossHotbar.BarId(set));
            afterSharedReload = Verify(module, plan);

            // Still worse than the raw write: put the live slots back so at least the memory is right.
            if (afterSharedReload < afterWrite)
            {
                foreach (var p in plan.Placements)
                    module->SetAndSaveSlot(p.BarId, (uint)p.Slot, p.Action.SlotType, p.Action.Id);
            }
        }

        var shared = string.Join(", ", plan.UsedSets().Select(set => $"set {set} shared={module->IsHotbarShared(CrossHotbar.BarId(set))}"));
        var diagnostics = $"afterWrite={afterWrite} afterReload={afterReload} afterSharedReload={afterSharedReload}; {shared}; pvp={Plugin.ClientState.IsPvP}; job={plan.Job.RowId}";
        var verified = Verify(module, plan);

        return new Result(cleared, written, null, verified, diagnostics);
    }

    private static int Verify(RaptureHotbarModule* module, Plan plan)
    {
        var ok = 0;
        foreach (var p in plan.Placements)
        {
            var s = module->GetSlotById(p.BarId, (uint)p.Slot);
            if (s != null && s->CommandType == p.Action.SlotType && s->CommandId == p.Action.Id)
                ok++;
        }
        return ok;
    }

    /// <summary>Live contents of a cross hotbar set, for the /xmapper dump diagnostic.</summary>
    public static List<string> Dump(int set)
    {
        var lines = new List<string>();
        var module = RaptureHotbarModule.Instance();
        if (module == null)
        {
            lines.Add("Hotbar module is not available.");
            return lines;
        }

        var bar = CrossHotbar.BarId(set);
        lines.Add($"Set {set} (bar {bar}), shared: {module->IsHotbarShared(bar)}");
        for (var slot = 0; slot < CrossHotbar.SlotsPerSet; slot++)
        {
            var s = module->GetSlotById(bar, (uint)slot);
            if (s == null) { lines.Add($"  {slot}: <null>"); continue; }
            var (half, cluster, i) = CrossHotbar.Decompose(slot);
            lines.Add($"  {slot} {half} {cluster} {i}: {s->CommandType} #{s->CommandId}");
        }
        return lines;
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
