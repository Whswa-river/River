using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RiverBox;

public static class SubmarineStatus
{
    public const uint ItemIdRepairMaterial = 10373; // 魔导机械修理材料
    public const uint ItemIdCeruleum = 10155;       // 桶装青磷水

    public static unsafe List<SubmarineVesselInfo> GetVesselData()
    {
        var result = new List<SubmarineVesselInfo>();
        var territory = HousingManager.Instance()->WorkshopTerritory;
        if (territory == null)
            return result;

        var data = territory->Submersible.Data;
        for (int i = 0; i < data.Length; i++)
        {
            var sub = data[i];
            if (sub.RankId == 0)
                continue;

            var returnTime = sub.ReturnTime;

            result.Add(new SubmarineVesselInfo
            {
                Name = $"艇{i + 1}",
                Level = sub.RankId,
                ReturnTime = returnTime,
                Destination = ""
            });
        }
        return result;
    }

    public static string FormatArrival(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return "-";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy/M/d HH:mm");
    }

    public static string FormatRemaining(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return "已完成";
        var remaining = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime() - DateTime.Now;
        if (remaining.TotalSeconds <= 0)
            return "已完成";
        return $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    /// <summary>
    /// 读取当前登录角色背包（Inventory1-4 四页）中修理材料与青磷水的数量。
    /// 未登录或读取失败返回 null。
    /// </summary>
    public static unsafe (int Repair, int Ceruleum)? GetInventoryCounts()
    {
        try
        {
            var manager = InventoryManager.Instance();
            if (manager == null)
                return null;

            int repair = 0, ceruleum = 0;
            for (int t = 0; t < 4; t++)
            {
                var container = manager->GetInventoryContainer((InventoryType)t);
                if (container == null || !container->IsLoaded)
                    continue;

                var items = container->Items;
                if (items == null)
                    continue;

                var size = container->Size;
                for (int i = 0; i < size; i++)
                {
                    var id = items[i].ItemId & 0x00FFFFFFu;
                    if (id == ItemIdRepairMaterial)
                        repair += items[i].Quantity;
                    else if (id == ItemIdCeruleum)
                        ceruleum += items[i].Quantity;
                }
            }
            return (repair, ceruleum);
        }
        catch (Exception e)
        {
            Svc.Log.Warning($"[SubmarineStatus] GetInventoryCounts failed: {e.Message}");
            return null;
        }
    }
}
