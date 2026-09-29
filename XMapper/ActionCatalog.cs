using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace XMapper;

public enum JobKind { Combat, Crafter, Gatherer }

public sealed record MappableAction(
    uint Id,
    string Name,
    uint IconId,
    int Level,
    Bucket Bucket,
    RaptureHotbarModule.HotbarSlotType SlotType,
    int RecastSeconds,
    bool IsGcd);

public sealed record JobInfo(uint RowId, string Abbreviation, string Name, int Level, JobKind Kind);

/// <summary>Collects and classifies every action the current class can put on a hotbar.</summary>
public static class ActionCatalog
{
    // Row ids in the ClassJob sheet.
    private const uint FirstCrafter = 8;   // CRP
    private const uint LastCrafter = 15;   // CUL
    private const uint FirstGatherer = 16; // MIN
    private const uint LastGatherer = 18;  // FSH

    // ActionCategory row ids.
    private const uint CategorySpell = 2;
    private const uint CategoryWeaponskill = 3;
    private const uint CategoryAbility = 4;

    private static readonly Dictionary<(uint category, string job), bool> CategoryCache = new();

    public static JobInfo? CurrentJob()
    {
        if (!Plugin.ClientState.IsLoggedIn) return null;
        var job = Plugin.PlayerState.ClassJob;
        if (!job.IsValid || job.RowId == 0) return null;

        var row = job.Value;
        var kind = row.RowId switch
        {
            >= FirstCrafter and <= LastCrafter => JobKind.Crafter,
            >= FirstGatherer and <= LastGatherer => JobKind.Gatherer,
            _ => JobKind.Combat,
        };

        return new JobInfo(row.RowId, row.Abbreviation.ExtractText(), row.Name.ExtractText(), Plugin.PlayerState.Level, kind);
    }

    public static List<MappableAction> Collect(JobInfo job, Bias bias, Configuration config)
    {
        return job.Kind switch
        {
            JobKind.Crafter => CollectCrafting(job, config),
            JobKind.Gatherer => CollectGathering(job),
            _ => CollectCombat(job, bias),
        };
    }

    // ------------------------------------------------------------------ combat

    private static unsafe List<MappableAction> CollectCombat(JobInfo job, Bias bias)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (sheet == null) return [];

        var eligible = new Dictionary<uint, Lumina.Excel.Sheets.Action>();
        foreach (var row in sheet)
        {
            if (!row.IsPlayerAction || row.IsPvP) continue;
            if (row.ClassJobLevel == 0 || row.ClassJobLevel > job.Level) continue;

            var cat = row.ActionCategory.RowId;
            if (cat is not (CategorySpell or CategoryWeaponskill or CategoryAbility)) continue;

            if (!JobInCategory(row.ClassJobCategory, job.Abbreviation)) continue;
            if (!IsUnlocked(row)) continue;

            eligible[row.RowId] = row;
        }

        var result = new List<MappableAction>();
        var manager = ActionManager.Instance();

        foreach (var (id, row) in eligible)
        {
            // Skip an action whose upgraded form is itself eligible; the game shows upgrades automatically anyway.
            if (manager != null)
            {
                var adjusted = manager->GetAdjustedActionId(id);
                if (adjusted != id && eligible.ContainsKey(adjusted)) continue;
            }

            var isGcd = row.ActionCategory.RowId != CategoryAbility;
            var recast = row.Recast100ms / 10;
            var bucket = ClassifyCombat(row, isGcd, recast, bias.RareThresholdSeconds);

            result.Add(new MappableAction(id, row.Name.ExtractText(), row.Icon, row.ClassJobLevel, bucket,
                RaptureHotbarModule.HotbarSlotType.Action, recast, isGcd));
        }

        return result.OrderBy(a => a.Level).ThenBy(a => a.Id).ToList();
    }

    private static Bucket ClassifyCombat(Lumina.Excel.Sheets.Action row, bool isGcd, int recast, int rareThreshold)
    {
        if (row.IsRoleAction) return Bucket.Role;
        if (recast >= rareThreshold) return Bucket.Rare;
        if (!isGcd) return Bucket.Ability;
        return row.CastType == 1 && row.EffectRange == 0 ? Bucket.SingleTarget : Bucket.Aoe;
    }

    private static unsafe bool IsUnlocked(Lumina.Excel.Sheets.Action row)
    {
        var link = row.UnlockLink.RowId;
        if (link == 0) return true;
        var ui = UIState.Instance();
        return ui == null || ui->IsUnlockLinkUnlockedOrQuestCompleted(link);
    }

    /// <summary>ClassJobCategory rows are a bag of booleans named by job abbreviation; look ours up by name.</summary>
    private static bool JobInCategory(Lumina.Excel.RowRef<ClassJobCategory> category, string abbreviation)
    {
        if (!category.IsValid) return false;
        var key = (category.RowId, abbreviation);
        if (CategoryCache.TryGetValue(key, out var cached)) return cached;

        var prop = typeof(ClassJobCategory).GetProperty(abbreviation, BindingFlags.Public | BindingFlags.Instance);
        var value = prop != null && prop.PropertyType == typeof(bool) && (bool)(prop.GetValue(category.Value) ?? false);
        CategoryCache[key] = value;
        return value;
    }

    // ------------------------------------------------------------------ crafting

    private static readonly string[] ProgressNames =
    [
        "Basic Synthesis", "Careful Synthesis", "Rapid Synthesis", "Groundwork", "Intensive Synthesis",
        "Prudent Synthesis", "Muscle Memory", "Delicate Synthesis", "Focused Synthesis",
    ];

    private static readonly string[] QualityNames =
    [
        "Basic Touch", "Standard Touch", "Advanced Touch", "Hasty Touch", "Precise Touch", "Prudent Touch",
        "Focused Touch", "Preparatory Touch", "Trained Finesse", "Byregot's Blessing", "Reflect", "Refined Touch",
        "Daring Touch", "Trained Eye", "Patient Touch",
    ];

    private static readonly string[] CraftBuffNames =
    [
        "Great Strides", "Innovation", "Veneration", "Waste Not", "Waste Not II", "Manipulation", "Master's Mend",
        "Immaculate Mend", "Tricks of the Trade", "Final Appraisal", "Observe", "Careful Observation",
        "Heart and Soul", "Quick Innovation", "Trained Perfection",
    ];

    private static List<MappableAction> CollectCrafting(JobInfo job, Configuration config)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<CraftAction>();
        if (sheet == null) return [];

        var result = new List<MappableAction>();
        foreach (var row in sheet)
        {
            if (row.ClassJob.RowId != job.RowId) continue;
            if (row.ClassJobLevel == 0 || row.ClassJobLevel > job.Level) continue;
            if (row.Specialist && !config.IncludeSpecialistActions) continue;

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var bucket = ProgressNames.Contains(name) ? Bucket.Progress
                : QualityNames.Contains(name) ? Bucket.Quality
                : CraftBuffNames.Contains(name) ? Bucket.Buff
                : Bucket.Other;

            result.Add(new MappableAction(row.RowId, name, row.Icon, row.ClassJobLevel, bucket,
                RaptureHotbarModule.HotbarSlotType.CraftAction, 0, true));
        }

        return result.OrderBy(a => a.Level).ThenBy(a => a.Id).ToList();
    }

    // ------------------------------------------------------------------ gathering

    private static readonly string[] GatherKeywords =
    [
        "Yield", "Harvest", "Tidings", "Bounty", "Solid Reason", "Ageless Words", "Collector", "Scour", "Scrutiny",
        "Meticulous", "Brazen", "Sharp Vision", "Field Mastery", "Clear Vision", "Wise to the World",
        "Cast", "Hook", "Mooch", "Hookset", "Prize Catch", "Chum", "Surface Slap", "Identical Cast",
    ];

    private static readonly string[] GatherBuffKeywords =
    [
        "Gift", "Ward", "Luck", "Twelve", "Patience", "Fish Eyes", "Thaliak", "Makeshift Bait", "Snagging", "Collect",
        "Nald'thal", "Nophica", "Blessed", "Eureka", "Bountiful", "Guidance",
    ];

    private static readonly string[] UtilityKeywords =
    [
        "Prospect", "Triangulate", "Lay of the Land", "Arbor Call", "Truth of", "Sneak", "Cast Light", "Release", "Quit",
        "Fathom", "Cordial", "Log", "Bait", "Stealth", "Prospector", "Mount", "Vantage",
    ];

    private static unsafe List<MappableAction> CollectGathering(JobInfo job)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (sheet == null) return [];

        var result = new List<MappableAction>();
        foreach (var row in sheet)
        {
            if (!row.IsPlayerAction || row.IsPvP) continue;
            if (row.ClassJobLevel == 0 || row.ClassJobLevel > job.Level) continue;
            if (!JobInCategory(row.ClassJobCategory, job.Abbreviation)) continue;
            if (!IsUnlocked(row)) continue;

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var bucket = GatherBuffKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)) ? Bucket.Buff
                : GatherKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)) ? Bucket.Gather
                : UtilityKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)) ? Bucket.Utility
                : Bucket.Other;

            result.Add(new MappableAction(row.RowId, name, row.Icon, row.ClassJobLevel, bucket,
                RaptureHotbarModule.HotbarSlotType.Action, row.Recast100ms / 10, true));
        }

        return result.OrderBy(a => a.Level).ThenBy(a => a.Id).ToList();
    }
}
