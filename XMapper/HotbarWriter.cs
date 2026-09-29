using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace XMapper;

/// <summary>
/// Writes a plan to the game's cross hotbars. The module-level SetAndSaveSlot call turned out to be a
/// no-op on cross hotbar ids, so each slot is written through a cascade of methods, verifying after each.
/// </summary>
public static unsafe class HotbarWriter
{
    public enum Method { None, SetAndSaveSlot, SlotSet, Fields }

    public sealed record Result(int Cleared, int Written, string? Error, int Verified = 0, string Diagnostics = "");

    public static Result Apply(Plan plan)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null)
            return new Result(0, 0, "Hotbar module is not available.");

        var job = plan.Job.RowId;
        var bias = plan.Bias;
        var counts = new Dictionary<Method, int>();
        void Count(Method m) => counts[m] = counts.GetValueOrDefault(m) + 1;

        var cleared = 0;
        if (bias.ClearWholeSets)
        {
            foreach (var set in plan.UsedSets())
            for (var slot = 0; slot < CrossHotbar.SlotsPerSet; slot++)
            {
                Count(WriteSlot(module, job, CrossHotbar.BarId(set), (uint)slot, RaptureHotbarModule.HotbarSlotType.Empty, 0));
                cleared++;
            }
        }
        else if (bias.ClearOwnedRegions)
        {
            foreach (var region in bias.OwnedRegions())
            for (var i = 0; i < CrossHotbar.SlotsPerRegion; i++)
            {
                Count(WriteSlot(module, job, CrossHotbar.BarId(region.Set), (uint)CrossHotbar.SlotIndex(region, i), RaptureHotbarModule.HotbarSlotType.Empty, 0));
                cleared++;
            }
        }

        var written = 0;
        foreach (var p in plan.Placements)
        {
            Count(WriteSlot(module, job, p.BarId, (uint)p.Slot, p.Action.SlotType, p.Action.Id));
            written++;
        }

        var afterWrite = Verify(module, plan);

        // Reload each set from saved data so the display refreshes. If that throws the writes away, put them back.
        foreach (var set in plan.UsedSets())
            module->LoadSavedHotbar(job, CrossHotbar.BarId(set));
        var afterReload = Verify(module, plan);

        if (afterReload < afterWrite)
        {
            foreach (var p in plan.Placements)
                WriteSlot(module, job, p.BarId, (uint)p.Slot, p.Action.SlotType, p.Action.Id);
        }

        var verified = Verify(module, plan);
        var methods = string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}"));
        var shared = string.Join(", ", plan.UsedSets().Select(set => $"set {set} shared={module->IsHotbarShared(CrossHotbar.BarId(set))}"));
        var diagnostics = $"methods[{methods}] afterWrite={afterWrite} afterReload={afterReload}; {shared}; pvp={Plugin.ClientState.IsPvP}; job={job}";

        return new Result(cleared, written, null, verified, diagnostics);
    }

    /// <summary>
    /// Write one slot, trying the module call first, then the slot's own Set, then raw fields.
    /// Returns the first method whose result reads back correctly.
    /// </summary>
    public static Method WriteSlot(RaptureHotbarModule* module, uint job, uint bar, uint slotId, RaptureHotbarModule.HotbarSlotType type, uint id)
    {
        var slot = module->GetSlotById(bar, slotId);
        if (slot == null) return Method.None;

        module->SetAndSaveSlot(bar, slotId, type, id);
        if (Matches(slot, type, id)) return Method.SetAndSaveSlot;

        slot->Set(type, id);
        if (Matches(slot, type, id))
        {
            module->WriteSavedSlot(job, bar, slotId, slot, false, false);
            return Method.SlotSet;
        }

        slot->CommandType = type;
        slot->CommandId = id;
        slot->OriginalApparentSlotType = type;
        slot->OriginalApparentActionId = id;
        slot->ApparentSlotType = type;
        slot->ApparentActionId = id;
        if (type == RaptureHotbarModule.HotbarSlotType.Empty)
            slot->IconId = 0;
        else
            slot->LoadIconId();
        slot->LoadCostDataForSlot(true);
        module->WriteSavedSlot(job, bar, slotId, slot, false, false);
        return Matches(slot, type, id) ? Method.Fields : Method.None;
    }

    private static bool Matches(RaptureHotbarModule.HotbarSlot* slot, RaptureHotbarModule.HotbarSlotType type, uint id) =>
        slot->CommandType == type && slot->CommandId == id;

    private static int Verify(RaptureHotbarModule* module, Plan plan)
    {
        var ok = 0;
        foreach (var p in plan.Placements)
        {
            var s = module->GetSlotById(p.BarId, (uint)p.Slot);
            if (s != null && Matches(s, p.Action.SlotType, p.Action.Id))
                ok++;
        }
        return ok;
    }

    /// <summary>What currently sits in a cross hotbar slot, for the preview's "kept" cells.</summary>
    public static (RaptureHotbarModule.HotbarSlotType type, uint id) Peek(int set, int slot)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null) return (RaptureHotbarModule.HotbarSlotType.Empty, 0);
        var s = module->GetSlotById(CrossHotbar.BarId(set), (uint)slot);
        return s == null ? (RaptureHotbarModule.HotbarSlotType.Empty, 0) : (s->CommandType, s->CommandId);
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
            lines.Add($"  {slot} {half} {cluster} {i}: {s->CommandType} #{s->CommandId} icon {s->IconId}");
        }
        return lines;
    }

    /// <summary>Write one slot step by step and report every stage, for the /xmapper probe diagnostic.</summary>
    public static List<string> Probe(uint job, int set, int slotId, uint actionId)
    {
        var lines = new List<string>();
        var module = RaptureHotbarModule.Instance();
        if (module == null) { lines.Add("Hotbar module is not available."); return lines; }

        var bar = CrossHotbar.BarId(set);
        var slot = module->GetSlotById(bar, (uint)slotId);
        if (slot == null) { lines.Add("GetSlotById returned null."); return lines; }
        var type = RaptureHotbarModule.HotbarSlotType.Action;

        lines.Add($"before: {slot->CommandType} #{slot->CommandId} icon {slot->IconId}");

        module->SetAndSaveSlot(bar, (uint)slotId, type, actionId);
        lines.Add($"after SetAndSaveSlot: {slot->CommandType} #{slot->CommandId} -> {(Matches(slot, type, actionId) ? "OK" : "no change")}");

        if (!Matches(slot, type, actionId))
        {
            slot->Set(type, actionId);
            lines.Add($"after slot->Set: {slot->CommandType} #{slot->CommandId} -> {(Matches(slot, type, actionId) ? "OK" : "no change")}");
        }

        if (!Matches(slot, type, actionId))
        {
            slot->CommandType = type;
            slot->CommandId = actionId;
            slot->OriginalApparentSlotType = type;
            slot->OriginalApparentActionId = actionId;
            slot->ApparentSlotType = type;
            slot->ApparentActionId = actionId;
            var iconOk = slot->LoadIconId();
            lines.Add($"after field write: {slot->CommandType} #{slot->CommandId} icon {slot->IconId} (LoadIconId {iconOk})");
        }

        module->WriteSavedSlot(job, bar, (uint)slotId, slot, false, false);
        lines.Add("WriteSavedSlot called.");

        module->LoadSavedHotbar(job, bar);
        lines.Add($"after LoadSavedHotbar: {slot->CommandType} #{slot->CommandId} icon {slot->IconId} -> {(Matches(slot, type, actionId) ? "kept" : "REVERTED")}");
        return lines;
    }
}
