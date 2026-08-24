using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECommons.DalamudServices;

namespace RiverBox;

public class SubmarineConfig
{
    public List<CharacterPreset> CharacterPresets { get; set; } = [];
    public List<SubmarineCacheData> CharacterCaches { get; set; } = [];
    public bool AutoCollectEnabled { get; set; } = false;
    public int DelayAfterRelogMs { get; set; } = 3000;
    public int DelayBetweenCallbacksMs { get; set; } = 500;
    public int DelayAfterCollectMs { get; set; } = 2000;

    private static string GetConfigPath()
    {
        return Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "SubmarineCollect.json");
    }

    public static SubmarineConfig Load()
    {
        try
        {
            var path = GetConfigPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SubmarineConfig>(json) ?? new SubmarineConfig();
            }
        }
        catch (Exception e)
        {
            Svc.Log.Error($"Failed to load SubmarineConfig: {e.Message}");
        }
        return new SubmarineConfig();
    }

    public void Save()
    {
        try
        {
            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception e)
        {
            Svc.Log.Error($"Failed to save SubmarineConfig: {e.Message}");
        }
    }

    public void AddPreset(string characterName, string worldName)
    {
        var preset = new CharacterPreset
        {
            CharacterName = characterName,
            WorldName = worldName,
            CreatedAt = DateTime.Now
        };

        if (!CharacterPresets.Exists(x =>
            x.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase) &&
            x.WorldName.Equals(worldName, StringComparison.OrdinalIgnoreCase)))
        {
            CharacterPresets.Add(preset);
            Save();
        }
    }

    public void RemovePreset(string characterName, string worldName)
    {
        CharacterPresets.RemoveAll(x =>
            x.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase) &&
            x.WorldName.Equals(worldName, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public string GetRelogCommand(CharacterPreset preset)
    {
        return $"/ays relog {preset.CharacterName}@{preset.WorldName}";
    }

    public void UpdateCache(string characterName, string worldName, List<SubmarineVesselInfo> vessels)
    {
        var existing = CharacterCaches.Find(x =>
            x.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase) &&
            x.WorldName.Equals(worldName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Vessels = vessels;
            existing.LastUpdateTime = DateTime.Now;
        }
        else
        {
            CharacterCaches.Add(new SubmarineCacheData
            {
                CharacterName = characterName,
                WorldName = worldName,
                Vessels = vessels,
                LastUpdateTime = DateTime.Now
            });
        }
        Save();
    }

    public SubmarineCacheData? GetCache(string characterName, string worldName)
    {
        return CharacterCaches.Find(x =>
            x.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase) &&
            x.WorldName.Equals(worldName, StringComparison.OrdinalIgnoreCase));
    }
}

public class CharacterPreset
{
    public string CharacterName { get; set; } = "";
    public string WorldName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string FullName => $"{CharacterName}@{WorldName}";

    public override string ToString() => FullName;
}

public class SubmarineCacheData
{
    public string CharacterName { get; set; } = "";
    public string WorldName { get; set; } = "";
    public List<SubmarineVesselInfo> Vessels { get; set; } = [];
    public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;

    [JsonIgnore]
    public string FullName => $"{CharacterName}@{WorldName}";
}

public class SubmarineVesselInfo
{
    public string Name { get; set; } = "";
    public int Level { get; set; } = 0;
    public long ReturnTime { get; set; } = 0;
    public string Destination { get; set; } = "";

    [JsonIgnore]
    public bool IsCompleted => ReturnTime > 0 && ReturnTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
