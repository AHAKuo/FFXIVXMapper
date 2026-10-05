using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace XMapper.Windows;

public class MainWindow : Window, IDisposable
{
    private static readonly string[] HalfNames = ["L2", "R2"];
    private static readonly string[] ClusterNames = ["D-pad", "Buttons"];
    private static readonly string[] SetNames = ["Set 1", "Set 2", "Set 3", "Set 4", "Set 5", "Set 6", "Set 7", "Set 8"];

    private static readonly Vector4 OwnedEmpty = new(0.18f, 0.18f, 0.22f, 1f);
    private static readonly Vector4 KeptCell = new(0.10f, 0.10f, 0.10f, 1f);
    private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.75f, 0.3f, 1f);
    private static readonly Vector4 Good = new(0.45f, 1f, 0.55f, 1f);

    private readonly Plugin plugin;
    private Plan? plan;
    private JobInfo? planJob;
    private string? planBiasName;
    private string editorNewName = string.Empty;
    private string? editingBiasName;
    private string status = string.Empty;
    private Vector4 statusColor = Muted;

    public MainWindow(Plugin plugin) : base("XMapper###XMapperMain")
    {
        this.plugin = plugin;
        Size = new Vector2(760, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 400), MaximumSize = new Vector2(3000, 3000) };
    }

    public void Dispose() { }

    private Configuration Config => plugin.Configuration;

    public override void OnOpen() => RefreshPlan();

    public override void Draw()
    {
        var job = ActionCatalog.CurrentJob();
        if (job == null)
        {
            ImGui.TextColored(Warn, "Log in to a character first.");
            return;
        }

        // Job changed under us: rebuild so the preview never shows another job's layout.
        if (planJob == null || planJob.RowId != job.RowId || planJob.Level != job.Level)
            RefreshPlan();

        DrawHeader(job);
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Preview", ImGuiTreeNodeFlags.DefaultOpen))
            DrawPreview();

        if (ImGui.CollapsingHeader("Biases"))
            DrawBiasEditor(job);

        if (ImGui.CollapsingHeader("Options"))
            DrawOptions();
    }

    // ------------------------------------------------------------------ header

    private void DrawHeader(JobInfo job)
    {
        var kind = job.Kind switch { JobKind.Crafter => "crafter", JobKind.Gatherer => "gatherer", _ => "combat" };
        ImGui.TextUnformatted($"{job.Name} ({job.Abbreviation}) Lv {job.Level}, {kind}");

        var biases = plugin.AllBiases();
        var names = biases.Select(b => b.Name).ToArray();
        var current = plugin.BiasFor(job);
        var index = Array.FindIndex(names, n => n == current.Name);
        if (index < 0) index = 0;

        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Bias for this job", ref index, names, names.Length))
        {
            plugin.SetBiasFor(job, biases[index]);
            RefreshPlan();
        }
        Tooltip("Remembered per job. Combat jobs default to AHA, crafters to Crafting, gatherers to Gathering.");

        ImGui.SameLine();
        if (ImGui.Button("Refresh preview"))
            RefreshPlan();

        ImGui.SameLine();
        var ctrl = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!ctrl || plan == null))
        {
            if (ImGui.Button("Apply to cross hotbars") && plan != null)
            {
                var result = plugin.ApplyPlan(plan);
                if (result.Error != null)
                {
                    status = result.Error;
                    statusColor = Warn;
                }
                else
                {
                    status = $"Wrote {result.Written} actions (cleared {result.Cleared} slots).";
                    statusColor = Good;
                }
            }
        }
        if (!ctrl && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl and click. This overwrites the slots shown in the preview.");

        if (plan != null)
        {
            var summary = $"{plan.Placements.Count} of {plan.ActionCount} actions placed on sets {string.Join(", ", plan.UsedSets())}";
            if (plan.Unplaced.Count > 0) summary += $", {plan.Unplaced.Count} unplaced";
            ImGui.TextColored(Muted, summary + ".");
        }

        if (!string.IsNullOrEmpty(status))
            ImGui.TextColored(statusColor, status);
    }

    private void RefreshPlan()
    {
        try
        {
            plan = plugin.BuildPlan();
            planJob = plan?.Job;
            planBiasName = plan?.Bias.Name;
            status = string.Empty;
        }
        catch (Exception ex)
        {
            plan = null;
            status = $"Could not build a plan: {ex.Message}";
            statusColor = Warn;
            Plugin.Log.Error(ex, "BuildPlan failed");
        }
    }

    // ------------------------------------------------------------------ preview

    private void DrawPreview()
    {
        if (plan == null)
        {
            ImGui.TextColored(Muted, "No plan yet.");
            return;
        }

        var iconSize = 36f * ImGuiHelpers.GlobalScale;
        foreach (var set in plan.UsedSets())
        {
            using var id = ImRaii.PushId(set);
            var flags = set <= 2 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.TreeNodeEx($"Cross Hotbar Set {set}", flags | ImGuiTreeNodeFlags.SpanAvailWidth))
                continue;

            using (var table = ImRaii.Table("regions", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("L2 D-pad");
                    ImGui.TableSetupColumn("L2 Buttons");
                    ImGui.TableSetupColumn("R2 D-pad");
                    ImGui.TableSetupColumn("R2 Buttons");
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();

                    foreach (var (half, cluster) in new[] { (Half.L2, Cluster.Dpad), (Half.L2, Cluster.Buttons), (Half.R2, Cluster.Dpad), (Half.R2, Cluster.Buttons) })
                    {
                        ImGui.TableNextColumn();
                        DrawRegion(set, half, cluster, iconSize);
                    }
                }
            }

            ImGui.TreePop();
        }

        if (plan.Unplaced.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Warn, $"Did not fit ({plan.Unplaced.Count}):");
            foreach (var group in plan.Unplaced.GroupBy(a => a.Bucket))
            {
                ImGui.BulletText($"{Bias.BucketLabel(group.Key)}: {string.Join(", ", group.Select(a => a.Name))}");
            }
            ImGui.TextColored(Muted, "Raise the bias's max set or add regions for these buckets.");
        }
    }

    private void DrawRegion(int set, Half half, Cluster cluster, float iconSize)
    {
        var owned = plan!.RegionOwned(set, half, cluster);
        var wholeSet = plan.Bias.ClearWholeSets;

        for (var i = 0; i < CrossHotbar.SlotsPerRegion; i++)
        {
            using var id = ImRaii.PushId(i);
            var slot = CrossHotbar.SlotIndex(half, cluster, i);
            var placement = plan.At(set, slot);
            var label = CrossHotbar.SlotLabel(cluster, i, Config.PlayStationLabels);

            if (i > 0) ImGui.SameLine();

            using (ImRaii.Group())
            {
                if (placement != null)
                {
                    DrawIcon(placement.Action.IconId, iconSize);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{placement.Action.Name}\n{Bias.BucketLabel(placement.Action.Bucket)}, Lv {placement.Action.Level}"
                                         + (placement.Action.RecastSeconds > 0 ? $", recast {placement.Action.RecastSeconds}s" : string.Empty));
                }
                else if (owned || wholeSet)
                {
                    DrawEmptyBox(iconSize, OwnedEmpty);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Will be emptied.");
                }
                else
                {
                    var (type, cmd) = HotbarWriter.Peek(set, slot);
                    DrawEmptyBox(iconSize, KeptCell);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(type == RaptureHotbarModule.HotbarSlotType.Empty ? "Not touched (empty)." : $"Not touched (keeps {type} #{cmd}).");
                }

                var textWidth = ImGui.CalcTextSize(label).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (iconSize - textWidth) / 2));
                ImGui.TextColored(Muted, label);
            }
        }
    }

    private static void DrawIcon(uint iconId, float size)
    {
        var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size, size));
    }

    private static void DrawEmptyBox(float size, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size, size));
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(size, size), ImGui.GetColorU32(color), 4f);
        dl.AddRect(pos, pos + new Vector2(size, size), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1f)), 4f);
    }

    // ------------------------------------------------------------------ bias editor

    private void DrawBiasEditor(JobInfo job)
    {
        var all = plugin.AllBiases();
        var names = all.Select(b => b.Name).ToArray();
        var editIndex = Array.FindIndex(names, n => n == (editingBiasName ?? plugin.BiasFor(job).Name));
        if (editIndex < 0) editIndex = 0;

        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Bias", ref editIndex, names, names.Length))
            editingBiasName = names[editIndex];

        var bias = all[editIndex];
        editingBiasName = bias.Name;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##newname", "copy name", ref editorNewName, 40);
        ImGui.SameLine();
        if (ImGui.Button("Duplicate"))
        {
            var name = string.IsNullOrWhiteSpace(editorNewName) ? $"{bias.Name} copy" : editorNewName.Trim();
            if (plugin.FindBias(name) == null)
            {
                Config.CustomBiases.Add(bias.Clone(name));
                Config.Save();
                editingBiasName = name;
                editorNewName = string.Empty;
            }
        }
        Tooltip("Built-in biases cannot be edited. Duplicate one, then change the copy.");

        if (!bias.BuiltIn)
        {
            ImGui.SameLine();
            if (ImGui.Button("Delete") && ImGui.GetIO().KeyCtrl)
            {
                Config.CustomBiases.Remove(bias);
                foreach (var key in Config.BiasByJob.Where(kv => kv.Value == bias.Name).Select(kv => kv.Key).ToList())
                    Config.BiasByJob.Remove(key);
                Config.Save();
                editingBiasName = null;
                RefreshPlan();
                return;
            }
            Tooltip("Hold Ctrl and click to delete.");
        }

        using var disabled = ImRaii.Disabled(bias.BuiltIn);
        var changed = false;

        var rare = bias.RareThresholdSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Rare threshold (s)", ref rare, 30, 300)) { bias.RareThresholdSeconds = rare; changed = true; }
        Tooltip("Combat actions with a recast at least this long count as rare, checked before anything else.");

        var maxSet = bias.MaxSet;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Max set", ref maxSet, 1, 8)) { bias.MaxSet = maxSet; changed = true; }
        Tooltip("Sets above this are never written, even if a region lists them.");

        var clearOwned = bias.ClearOwnedRegions;
        if (ImGui.Checkbox("Empty owned regions before writing", ref clearOwned)) { bias.ClearOwnedRegions = clearOwned; changed = true; }
        ImGui.SameLine(300);
        var clearAll = bias.ClearWholeSets;
        if (ImGui.Checkbox("Empty whole sets", ref clearAll)) { bias.ClearWholeSets = clearAll; changed = true; }
        Tooltip("Also wipes slots on the used sets that no region owns (items, mounts, macros you placed by hand).");

        var first = (int)bias.FirstSlot;
        var directionNames = Enum.GetValues<Direction>().Select(d => CrossHotbar.DirectionLabel(d, Config.PlayStationLabels)).ToArray();
        ImGui.SetNextItemWidth(260);
        if (ImGui.Combo("First slot", ref first, directionNames, directionNames.Length)) { bias.FirstSlot = (Direction)first; changed = true; }
        Tooltip("The lowest-level action of each bucket (the combo opener) goes here on every region; the rest follow clockwise.");

        ImGui.Spacing();
        ImGui.TextColored(Muted, "Regions per bucket, in fill order. Overflow moves to the next region in the list.");
        ImGui.TextColored(Muted, "Heal buckets without regions fall back to the attack buckets, so non-healer biases need not list them.");

        foreach (var bucket in Enum.GetValues<Bucket>())
        {
            var relevant = bucket switch
            {
                Bucket.SingleTarget or Bucket.Aoe or Bucket.Ability or Bucket.Role or Bucket.Rare or Bucket.Heal or Bucket.AoeHeal
                    => bias.IsForCombat || !(bias.IsForCrafting || bias.IsForGathering),
                Bucket.Progress or Bucket.Quality => bias.IsForCrafting || !(bias.IsForCombat || bias.IsForGathering),
                Bucket.Gather or Bucket.Utility => bias.IsForGathering || !(bias.IsForCombat || bias.IsForCrafting),
                _ => true,
            };
            if (!relevant) continue;

            using var bucketId = ImRaii.PushId((int)bucket);
            var rule = bias.Rules.FirstOrDefault(r => r.Bucket == bucket);
            var count = rule?.Regions.Count ?? 0;

            if (!ImGui.TreeNodeEx($"{Bias.BucketLabel(bucket)} ({count} region{(count == 1 ? "" : "s")})"))
                continue;

            rule ??= bias.RuleFor(bucket);
            var removeAt = -1;
            for (var i = 0; i < rule.Regions.Count; i++)
            {
                using var rowId = ImRaii.PushId(i);
                var r = rule.Regions[i];

                var set = r.Set - 1;
                ImGui.SetNextItemWidth(80);
                if (ImGui.Combo("##set", ref set, SetNames, SetNames.Length)) { r.Set = set + 1; changed = true; }
                ImGui.SameLine();
                var half = (int)r.Half;
                ImGui.SetNextItemWidth(60);
                if (ImGui.Combo("##half", ref half, HalfNames, HalfNames.Length)) { r.Half = (Half)half; changed = true; }
                ImGui.SameLine();
                var cluster = (int)r.Cluster;
                ImGui.SetNextItemWidth(90);
                if (ImGui.Combo("##cluster", ref cluster, ClusterNames, ClusterNames.Length)) { r.Cluster = (Cluster)cluster; changed = true; }
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) removeAt = i;
                if (r.Set > bias.MaxSet)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Warn, "above max set");
                }
            }

            if (removeAt >= 0) { rule.Regions.RemoveAt(removeAt); changed = true; }

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                rule.Regions.Add(new Region(1, Half.R2, Cluster.Buttons));
                changed = true;
            }
            Tooltip("Add a region");

            ImGui.TreePop();
        }

        if (changed)
        {
            Config.Save();
            if (plan?.Bias.Name == bias.Name) RefreshPlan();
        }
    }

    // ------------------------------------------------------------------ options

    private void DrawOptions()
    {
        var ps = Config.PlayStationLabels;
        if (ImGui.Checkbox("PlayStation button symbols", ref ps)) { Config.PlayStationLabels = ps; Config.Save(); }

        var spec = Config.IncludeSpecialistActions;
        if (ImGui.Checkbox("Include specialist crafting actions", ref spec)) { Config.IncludeSpecialistActions = spec; Config.Save(); RefreshPlan(); }

        var chat = Config.ChatSummary;
        if (ImGui.Checkbox("Print a summary to chat after applying", ref chat)) { Config.ChatSummary = chat; Config.Save(); }

        ImGui.Spacing();
        ImGui.TextColored(Muted, "Slot order per set: L2 d-pad, L2 buttons, R2 d-pad, R2 buttons; d-pad up/right/down/left, buttons top/right/bottom/left.");
        ImGui.TextColored(Muted, "Healer jobs (ClassJob role 4) default to the AHA Healer bias: attacks on the d-pads, heals on the face buttons.");
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }
}
