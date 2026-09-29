using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace XMapper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

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

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
