using System.Collections.Generic;
using ECommons.Configuration;

namespace RiverBox;

public class RiverBoxConfig
{
    public Dictionary<string, string> PluginCustomNames = [];
    public Dictionary<string, List<string>> PluginGroups = [];
    // 插件置顶/排序顺序（原先是 PluginManager 的静态内存列表，重启后丢失）
    public List<string> PluginPinnedOrder = [];

    // 场地标点传送：留空使用 vnavmesh 导航；填入 TP 指令（如 /pdrtp pos）则直接传送，无需 vnavmesh
    public string FieldMarkerTpCommand = "";

    public void Save() => EzConfig.Save();
}
