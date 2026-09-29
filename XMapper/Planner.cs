using System.Collections.Generic;
using System.Linq;

namespace XMapper;

public sealed record Placement(int Set, int Slot, MappableAction Action)
{
    public uint BarId => CrossHotbar.BarId(Set);
}

public sealed class Plan
{
    public JobInfo Job { get; init; } = null!;
    public Bias Bias { get; init; } = null!;
    public List<Placement> Placements { get; } = [];
    public List<MappableAction> Unplaced { get; } = [];
    public int ActionCount { get; init; }

    public Placement? At(int set, int slot) => Placements.FirstOrDefault(p => p.Set == set && p.Slot == slot);

    /// <summary>Sets that will be written to (owned regions or whole-set clearing).</summary>
    public IEnumerable<int> UsedSets()
    {
        var sets = new SortedSet<int>();
        foreach (var r in Bias.OwnedRegions()) sets.Add(r.Set);
        foreach (var p in Placements) sets.Add(p.Set);
        return sets;
    }

    public bool RegionOwned(int set, Half half, Cluster cluster) =>
        Bias.OwnedRegions().Any(r => r.Set == set && r.Half == half && r.Cluster == cluster);
}

/// <summary>Turns a classified action list plus a bias into concrete slot placements.</summary>
public static class Planner
{
    public static Plan Build(JobInfo job, Bias bias, List<MappableAction> actions)
    {
        var plan = new Plan { Job = job, Bias = bias, ActionCount = actions.Count };
        var taken = new HashSet<(int set, int slot)>();

        foreach (var group in actions.GroupBy(a => a.Bucket).OrderBy(g => g.Key))
        {
            var regions = bias.RegionsFor(group.Key);
            var queue = new Queue<MappableAction>(group.OrderBy(a => a.Level).ThenBy(a => a.Id));

            foreach (var region in regions)
            {
                for (var i = 0; i < CrossHotbar.SlotsPerRegion && queue.Count > 0; i++)
                {
                    var slot = CrossHotbar.SlotIndex(region, i);
                    if (!taken.Add((region.Set, slot))) continue; // region shared by two buckets; first come first served
                    plan.Placements.Add(new Placement(region.Set, slot, queue.Dequeue()));
                }
                if (queue.Count == 0) break;
            }

            plan.Unplaced.AddRange(queue);
        }

        return plan;
    }
}
