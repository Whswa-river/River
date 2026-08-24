using System.Collections.Generic;
using ECommons.Configuration;

namespace RiverBox;

public class RiverBoxConfig
{
    public Dictionary<string, string> PluginCustomNames = [];
    public Dictionary<string, List<string>> PluginGroups = [];

    public void Save() => EzConfig.Save();
}
