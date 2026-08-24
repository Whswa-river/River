using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Dalamud.Plugin;
using ECommons.Automation;
using ECommons.DalamudServices;

namespace RiverBox;

public class PluginInfo
{
    public string Name = "";
    public string InternalName = "";
    public bool IsLoaded;
}

public class ProfileInfo
{
    public string Name = "";
    public bool IsEnabled;
    public bool IsDefaultProfile;
}

public static class PluginManager
{
    private static readonly List<string> _customOrder = [];

    public static List<PluginInfo> GetPlugins()
    {
        var all = Svc.PluginInterface.InstalledPlugins
            .Select(x => new PluginInfo { Name = x.Name, InternalName = x.InternalName, IsLoaded = x.IsLoaded })
            .ToList();

        var pinned = new List<PluginInfo>();
        var unpinned = new List<PluginInfo>();

        foreach (var p in all)
        {
            if (_customOrder.Contains(p.InternalName))
                pinned.Add(p);
            else
                unpinned.Add(p);
        }

        pinned.Sort((a, b) => _customOrder.IndexOf(a.InternalName).CompareTo(_customOrder.IndexOf(b.InternalName)));
        unpinned.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        var result = new List<PluginInfo>();
        result.AddRange(pinned);
        result.AddRange(unpinned);
        return result;
    }

    public static void PinToTop(string internalName)
    {
        _customOrder.Remove(internalName);
        _customOrder.Insert(0, internalName);
    }

    public static void Unpin(string internalName)
    {
        _customOrder.Remove(internalName);
    }

    public static void MoveInCustomOrder(string internalName, int newIndex)
    {
        _customOrder.Remove(internalName);
        if (newIndex < 0) newIndex = 0;
        if (newIndex > _customOrder.Count) newIndex = _customOrder.Count;
        _customOrder.Insert(newIndex, internalName);
    }

    public static int GetCustomOrderIndex(string internalName) => _customOrder.IndexOf(internalName);

    public static bool IsPinned(string internalName) => _customOrder.Contains(internalName);

    public static void EnablePlugin(string internalName) => Chat.ExecuteCommand($"/xlenableplugin \"{internalName}\"");

    public static void DisablePlugin(string internalName) => Chat.ExecuteCommand($"/xldisableplugin \"{internalName}\"");

    public static void TogglePlugin(string internalName) => Chat.ExecuteCommand($"/xltoggleplugin \"{internalName}\"");

    public static void OpenPlugin(string internalName)
    {
        try
        {
            var pmType = typeof(IDalamudPluginInterface).Assembly.GetType("Dalamud.Plugin.Internal.PluginManager");
            if (pmType == null) return;

            var instancesProp = pmType.GetProperty("Instances", BindingFlags.Public | BindingFlags.Static);
            if (instancesProp == null) return;

            var instances = instancesProp.GetValue(null) as IEnumerable;
            if (instances == null) return;

            foreach (var inst in instances)
            {
                var type = inst.GetType();
                var pluginManager = type.GetProperty("PluginManager")?.GetValue(inst);
                if (pluginManager == null) continue;

                var pm2Type = pluginManager.GetType();
                var listProp = pm2Type.GetProperty("InstalledPlugins", BindingFlags.Public | BindingFlags.Instance);
                if (listProp == null) continue;

                var plugins = listProp.GetValue(pluginManager) as IEnumerable;
                if (plugins == null) continue;

                foreach (var p in plugins)
                {
                    var pType = p.GetType();
                    var name = pType.GetProperty("InternalName")?.GetValue(p) as string;
                    if (name == internalName)
                    {
                        var configUiMethod = pType.GetMethod("LoadConfigUI", BindingFlags.Public | BindingFlags.Instance)
                                          ?? pType.GetMethod("OpenConfigUi", BindingFlags.Public | BindingFlags.Instance);
                        configUiMethod?.Invoke(p, null);
                        return;
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static List<ProfileInfo> _profileCache = [];
    private static DateTime _profileCacheTime;

    public static List<ProfileInfo> GetProfiles()
    {
        if ((DateTime.Now - _profileCacheTime).TotalSeconds < 3)
            return _profileCache;

        _profileCache = RefreshProfiles();
        _profileCacheTime = DateTime.Now;
        return _profileCache;
    }

    private static List<ProfileInfo> RefreshProfiles()
    {
        var result = new List<ProfileInfo>();
        try
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncherCN", "dalamudConfig", "profiles");
            if (!Directory.Exists(configDir))
                configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XIVLauncher", "dalamudConfig", "profiles");
            if (!Directory.Exists(configDir))
                return result;

            foreach (var dir in Directory.GetDirectories(configDir))
            {
                var metaFile = Path.Combine(dir, "profile.json");
                if (!File.Exists(metaFile))
                    continue;

                try
                {
                    var json = File.ReadAllText(metaFile);
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var name = Path.GetFileName(dir);
                    if (root.TryGetProperty("Name", out var nameProp))
                        name = nameProp.GetString() ?? name;

                    bool isEnabled = false;
                    if (root.TryGetProperty("IsEnabled", out var enabledProp))
                        isEnabled = enabledProp.GetBoolean();

                    bool isDefault = false;
                    if (root.TryGetProperty("IsDefaultProfile", out var defaultProp))
                        isDefault = defaultProp.GetBoolean();

                    result.Add(new ProfileInfo { Name = name, IsEnabled = isEnabled, IsDefaultProfile = isDefault });
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return result;
    }

    public static void EnableCollection(string name) { }

    public static void DisableCollection(string name) { }

    public static void ToggleCollection(string name) { }

    public static string GetCustomName(string internalName)
    {
        if (RiverBox.C.PluginCustomNames.TryGetValue(internalName, out var customName) && !string.IsNullOrEmpty(customName))
            return customName;
        return internalName;
    }

    public static void SetCustomName(string internalName, string customName)
    {
        if (string.IsNullOrWhiteSpace(customName) || customName == internalName)
            RiverBox.C.PluginCustomNames.Remove(internalName);
        else
            RiverBox.C.PluginCustomNames[internalName] = customName;
        RiverBox.C.Save();
    }

    public static List<string> GetGroupNames() => RiverBox.C.PluginGroups.Keys.ToList();

    public static List<string> GetPluginsInGroup(string groupName)
    {
        return RiverBox.C.PluginGroups.TryGetValue(groupName, out var list) ? list : [];
    }

    public static void CreateGroup(string groupName)
    {
        if (!string.IsNullOrWhiteSpace(groupName) && !RiverBox.C.PluginGroups.ContainsKey(groupName))
        {
            RiverBox.C.PluginGroups[groupName] = [];
            RiverBox.C.Save();
        }
    }

    public static void DeleteGroup(string groupName)
    {
        RiverBox.C.PluginGroups.Remove(groupName);
        RiverBox.C.Save();
    }

    public static void RenameGroup(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || oldName == newName) return;
        if (RiverBox.C.PluginGroups.TryGetValue(oldName, out var list) && !RiverBox.C.PluginGroups.ContainsKey(newName))
        {
            RiverBox.C.PluginGroups.Remove(oldName);
            RiverBox.C.PluginGroups[newName] = list;
            RiverBox.C.Save();
        }
    }

    public static void AddToGroup(string groupName, string internalName)
    {
        if (!RiverBox.C.PluginGroups.ContainsKey(groupName)) return;
        if (!RiverBox.C.PluginGroups[groupName].Contains(internalName))
        {
            RiverBox.C.PluginGroups[groupName].Add(internalName);
            RiverBox.C.Save();
        }
    }

    public static void RemoveFromGroup(string groupName, string internalName)
    {
        if (RiverBox.C.PluginGroups.TryGetValue(groupName, out var list))
        {
            list.Remove(internalName);
            RiverBox.C.Save();
        }
    }

    public static string GetPluginGroup(string internalName)
    {
        foreach (var kv in RiverBox.C.PluginGroups)
        {
            if (kv.Value.Contains(internalName))
                return kv.Key;
        }
        return "";
    }
}
