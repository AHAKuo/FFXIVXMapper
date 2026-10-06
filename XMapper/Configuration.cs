using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace XMapper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>1: Direction enum re-numbered to the game's slot order (Left=0, Up=1, Right=2, Down=3).</summary>
    public int Version { get; set; } = 1;

    public const int CurrentVersion = 1;

    /// <summary>User-made biases. Built-ins are regenerated in code and never stored.</summary>
    public List<Bias> CustomBiases { get; set; } = [];

    /// <summary>Bias name chosen per class/job row id. Falls back to a built-in matching the job type.</summary>
    public Dictionary<uint, string> BiasByJob { get; set; } = new();

    /// <summary>Show PlayStation symbols instead of Xbox letters for face buttons.</summary>
    public bool PlayStationLabels { get; set; } = false;

    /// <summary>Include crafting actions that need a specialist soul.</summary>
    public bool IncludeSpecialistActions { get; set; } = false;

    /// <summary>Print a summary to the chat log after applying.</summary>
    public bool ChatSummary { get; set; } = true;

    /// <summary>Bring an older config up to date. Returns true when something changed and a save is due.</summary>
    public bool Migrate()
    {
        if (Version >= CurrentVersion) return false;

        if (Version < 1)
        {
            // v0.2.0 numbered Direction as Up=0, Right=1, Down=2, Left=3; it is now Left=0, Up=1, Right=2, Down=3.
            foreach (var bias in CustomBiases)
                bias.FirstSlot = (int)bias.FirstSlot switch { 0 => Direction.Up, 1 => Direction.Right, 2 => Direction.Down, _ => Direction.Left };
        }

        Version = CurrentVersion;
        return true;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
