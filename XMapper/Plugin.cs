using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XMapper.Windows;

namespace XMapper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/xmapper";

    public Configuration Configuration { get; }
    public readonly WindowSystem WindowSystem = new("XMapper");
    private MainWindow MainWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open XMapper. \"/xmapper map\" applies the current job's bias right away; \"/xmapper map <bias>\" picks one.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;

        Log.Information("XMapper loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    public void ToggleMainUi() => MainWindow.Toggle();

    // ------------------------------------------------------------------ biases

    public List<Bias> AllBiases()
    {
        var list = Bias.BuiltIns();
        list.AddRange(Configuration.CustomBiases);
        return list;
    }

    public Bias? FindBias(string name) =>
        AllBiases().FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bias to use for a job: the user's pick for that job, else the built-in matching its kind.</summary>
    public Bias BiasFor(JobInfo job)
    {
        if (Configuration.BiasByJob.TryGetValue(job.RowId, out var name))
        {
            var chosen = FindBias(name);
            if (chosen != null) return chosen;
        }

        return job.Kind switch
        {
            JobKind.Crafter => Bias.Crafting(),
            JobKind.Gatherer => Bias.Gathering(),
            _ => job.IsHealer ? Bias.AhaHealer() : Bias.Aha(),
        };
    }

    public void SetBiasFor(JobInfo job, Bias bias)
    {
        Configuration.BiasByJob[job.RowId] = bias.Name;
        Configuration.Save();
    }

    // ------------------------------------------------------------------ mapping

    public Plan? BuildPlan(Bias? biasOverride = null)
    {
        var job = ActionCatalog.CurrentJob();
        if (job == null) return null;
        var bias = biasOverride ?? BiasFor(job);
        var actions = ActionCatalog.Collect(job, bias, Configuration);
        return Planner.Build(job, bias, actions);
    }

    public HotbarWriter.Result ApplyPlan(Plan plan)
    {
        var result = HotbarWriter.Apply(plan);
        if (result.Error != null)
        {
            ChatGui.PrintError($"XMapper: {result.Error}");
            Log.Warning("Apply failed: {Error}", result.Error);
            return result;
        }

        var msg = $"{plan.Job.Abbreviation}: placed {result.Written} of {plan.ActionCount} actions with bias \"{plan.Bias.Name}\""
                  + (plan.Unplaced.Count > 0 ? $", {plan.Unplaced.Count} did not fit." : ".");
        Log.Information(msg);
        Log.Information("Verified {Verified}/{Written} live slots. {Diag}", result.Verified, result.Written, result.Diagnostics);
        if (Configuration.ChatSummary)
            ChatGui.Print(msg, "XMapper");

        if (result.Verified < result.Written)
            ChatGui.PrintError($"XMapper: only {result.Verified} of {result.Written} slots read back correctly ({result.Diagnostics}). Run /xmapper dump and report it.");
        return result;
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            ToggleMainUi();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "map":
            {
                Bias? bias = null;
                if (parts.Length > 1)
                {
                    bias = FindBias(parts[1]);
                    if (bias == null)
                    {
                        ChatGui.PrintError($"XMapper: no bias named \"{parts[1]}\". Known: {string.Join(", ", AllBiases().Select(b => b.Name))}");
                        return;
                    }
                }

                var plan = BuildPlan(bias);
                if (plan == null)
                {
                    ChatGui.PrintError("XMapper: not logged in or no job detected.");
                    return;
                }

                ApplyPlan(plan);
                break;
            }
            case "probe":
            {
                // /xmapper probe <set> <slot> <actionId>
                var a = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
                if (a.Length != 3 || !int.TryParse(a[0], out var set) || !int.TryParse(a[1], out var slot) || !uint.TryParse(a[2], out var actionId))
                {
                    ChatGui.PrintError("XMapper: usage /xmapper probe <set 1-8> <slot 0-15> <actionId>");
                    return;
                }

                var job = ActionCatalog.CurrentJob();
                if (job == null) { ChatGui.PrintError("XMapper: not logged in."); return; }

                foreach (var line in HotbarWriter.Probe(job.RowId, set, slot, actionId))
                {
                    Log.Information(line);
                    ChatGui.Print(line, "XMapper");
                }
                break;
            }
            case "dump":
            {
                var sets = parts.Length > 1 && int.TryParse(parts[1], out var n) ? [n] : new[] { 1, 2 };
                foreach (var set in sets)
                {
                    foreach (var line in HotbarWriter.Dump(set))
                    {
                        Log.Information(line);
                        ChatGui.Print(line, "XMapper");
                    }
                }
                break;
            }
            default:
                ToggleMainUi();
                break;
        }
    }
}
