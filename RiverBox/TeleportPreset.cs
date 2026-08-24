using System.Collections.Generic;

namespace RiverBox;

public class TeleportGroup
{
    public string Name { get; set; } = "新分组";
    public bool IsSystem { get; set; } = false;
    public List<TeleportButtonData> Buttons { get; set; } = new();
}

public class TeleportButtonData
{
    public string Name { get; set; } = "新按钮";
    public string AetheryteName { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public bool ShowInFloating { get; set; } = true;
    public string CustomCommand { get; set; } = "";
}

public class TeleportConfig
{
    public bool ModuleEnabled { get; set; } = false;
    public List<TeleportGroup> Groups { get; set; } = new();
}
