using System;
using System.Collections.Generic;
using System.Linq;

namespace XMapper;

/// <summary>What kind of action something is. Combat jobs use the first five; crafters and gatherers the rest.</summary>
public enum Bucket
{
    SingleTarget,
    Aoe,
    Ability,
    Role,
    Rare,
    Progress,
    Quality,
    Buff,
    Gather,
    Utility,
    Other,
}

public enum Half { L2, R2 }

public enum Cluster { Dpad, Buttons }

/// <summary>Four slots on one cross hotbar set: one half (trigger) and one cluster (d-pad or face buttons).</summary>
[Serializable]
public class Region
{
    /// <summary>Cross hotbar set, 1-based (1..8).</summary>
    public int Set { get; set; } = 1;
    public Half Half { get; set; } = Half.R2;
    public Cluster Cluster { get; set; } = Cluster.Buttons;

    public Region() { }

    public Region(int set, Half half, Cluster cluster)
    {
        Set = set;
        Half = half;
        Cluster = cluster;
    }

    public Region Clone() => new(Set, Half, Cluster);

    public override string ToString() => $"Set {Set} {Half} {(Cluster == Cluster.Dpad ? "D-pad" : "Buttons")}";

    public bool SameAs(Region other) => Set == other.Set && Half == other.Half && Cluster == other.Cluster;
}

[Serializable]
public class BucketRule
{
    public Bucket Bucket { get; set; }
    public List<Region> Regions { get; set; } = [];
}

/// <summary>
/// A bias is a preset: for each bucket, an ordered list of regions to fill, plus a few knobs.
/// </summary>
[Serializable]
public class Bias
{
    public string Name { get; set; } = "New bias";

    /// <summary>Built-in biases are not editable; duplicate them to change.</summary>
    public bool BuiltIn { get; set; }

    /// <summary>Actions whose recast is at least this many seconds count as "rare".</summary>
    public int RareThresholdSeconds { get; set; } = 90;

    /// <summary>Highest cross hotbar set this bias may write to. Regions on later sets are ignored.</summary>
    public int MaxSet { get; set; } = 2;

    /// <summary>Empty every slot of every region this bias owns before writing.</summary>
    public bool ClearOwnedRegions { get; set; } = true;

    /// <summary>Also empty the slots on the used sets that no region owns.</summary>
    public bool ClearWholeSets { get; set; } = false;

    /// <summary>When a bucket's regions are full, put the leftovers in any free owned slot instead of dropping them.</summary>
    public bool FillLeftoverSlots { get; set; } = true;

    public List<BucketRule> Rules { get; set; } = [];

    public List<Region> RegionsFor(Bucket bucket) =>
        Rules.FirstOrDefault(r => r.Bucket == bucket)?.Regions.Where(r => r.Set >= 1 && r.Set <= MaxSet).ToList() ?? [];

    public BucketRule RuleFor(Bucket bucket)
    {
        var rule = Rules.FirstOrDefault(r => r.Bucket == bucket);
        if (rule == null)
        {
            rule = new BucketRule { Bucket = bucket };
            Rules.Add(rule);
        }
        return rule;
    }

    /// <summary>Every region this bias touches, within MaxSet, de-duplicated.</summary>
    public IEnumerable<Region> OwnedRegions()
    {
        var seen = new List<Region>();
        foreach (var rule in Rules)
        foreach (var r in rule.Regions)
        {
            if (r.Set < 1 || r.Set > MaxSet) continue;
            if (seen.Any(s => s.SameAs(r))) continue;
            seen.Add(r);
            yield return r;
        }
    }

    public Bias Clone(string? newName = null)
    {
        return new Bias
        {
            Name = newName ?? Name,
            BuiltIn = false,
            RareThresholdSeconds = RareThresholdSeconds,
            MaxSet = MaxSet,
            ClearOwnedRegions = ClearOwnedRegions,
            ClearWholeSets = ClearWholeSets,
            FillLeftoverSlots = FillLeftoverSlots,
            Rules = Rules.Select(r => new BucketRule
            {
                Bucket = r.Bucket,
                Regions = r.Regions.Select(x => x.Clone()).ToList(),
            }).ToList(),
        };
    }

    public bool IsForCrafting => Rules.Any(r => r.Bucket is Bucket.Progress or Bucket.Quality);
    public bool IsForGathering => Rules.Any(r => r.Bucket is Bucket.Gather);
    public bool IsForCombat => Rules.Any(r => r.Bucket is Bucket.SingleTarget or Bucket.Aoe);

    // ------------------------------------------------------------------ built-ins

    private static List<Region> Alternating(int firstSet, Half half, Cluster cluster, int maxSet = 8)
    {
        var list = new List<Region>();
        for (var s = firstSet; s <= maxSet; s += 2)
            list.Add(new Region(s, half, cluster));
        return list;
    }

    private static List<Region> AlternatingPair(int firstSet, (Half, Cluster) a, (Half, Cluster) b, int maxSet = 8)
    {
        var list = new List<Region>();
        for (var s = firstSet; s <= maxSet; s += 2)
        {
            list.Add(new Region(s, a.Item1, a.Item2));
            list.Add(new Region(s, b.Item1, b.Item2));
        }
        return list;
    }

    public static Bias Aha(bool mirrored = false)
    {
        var right = mirrored ? Half.L2 : Half.R2;
        var left = mirrored ? Half.R2 : Half.L2;
        return new Bias
        {
            Name = mirrored ? "AHA Mirrored" : "AHA",
            BuiltIn = true,
            RareThresholdSeconds = 90,
            MaxSet = 2,
            Rules =
            [
                new BucketRule { Bucket = Bucket.SingleTarget, Regions = Alternating(1, right, Cluster.Buttons) },
                new BucketRule { Bucket = Bucket.Aoe, Regions = Alternating(1, left, Cluster.Buttons) },
                new BucketRule { Bucket = Bucket.Ability, Regions = AlternatingPair(1, (left, Cluster.Dpad), (right, Cluster.Dpad)) },
                new BucketRule { Bucket = Bucket.Role, Regions = AlternatingPair(2, (right, Cluster.Buttons), (right, Cluster.Dpad)) },
                new BucketRule { Bucket = Bucket.Rare, Regions = AlternatingPair(2, (left, Cluster.Buttons), (left, Cluster.Dpad)) },
            ],
        };
    }

    public static Bias Crafting() => new()
    {
        Name = "Crafting",
        BuiltIn = true,
        MaxSet = 2,
        Rules =
        [
            new BucketRule { Bucket = Bucket.Progress, Regions = Alternating(1, Half.R2, Cluster.Buttons) },
            new BucketRule { Bucket = Bucket.Quality, Regions = AlternatingPair(1, (Half.L2, Cluster.Buttons), (Half.L2, Cluster.Dpad)) },
            new BucketRule { Bucket = Bucket.Buff, Regions = AlternatingPair(1, (Half.R2, Cluster.Dpad), (Half.L2, Cluster.Dpad)) },
            new BucketRule { Bucket = Bucket.Other, Regions = AlternatingPair(2, (Half.R2, Cluster.Buttons), (Half.R2, Cluster.Dpad)) },
        ],
    };

    public static Bias Gathering() => new()
    {
        Name = "Gathering",
        BuiltIn = true,
        MaxSet = 2,
        Rules =
        [
            new BucketRule { Bucket = Bucket.Gather, Regions = AlternatingPair(1, (Half.R2, Cluster.Buttons), (Half.R2, Cluster.Dpad)) },
            new BucketRule { Bucket = Bucket.Buff, Regions = Alternating(1, Half.L2, Cluster.Buttons) },
            new BucketRule { Bucket = Bucket.Utility, Regions = Alternating(1, Half.L2, Cluster.Dpad) },
            new BucketRule { Bucket = Bucket.Other, Regions = AlternatingPair(2, (Half.R2, Cluster.Buttons), (Half.R2, Cluster.Dpad)) },
        ],
    };

    public static List<Bias> BuiltIns() => [Aha(), Aha(mirrored: true), Crafting(), Gathering()];

    public static string BucketLabel(Bucket b) => b switch
    {
        Bucket.SingleTarget => "Single-target attacks",
        Bucket.Aoe => "AoE attacks",
        Bucket.Ability => "Abilities (oGCD)",
        Bucket.Role => "Role actions",
        Bucket.Rare => "Rare (long cooldown)",
        Bucket.Progress => "Progress (synthesis)",
        Bucket.Quality => "Quality (touch)",
        Bucket.Buff => "Buffs / upkeep",
        Bucket.Gather => "Gather / yield",
        Bucket.Utility => "Utility",
        Bucket.Other => "Other",
        _ => b.ToString(),
    };
}
