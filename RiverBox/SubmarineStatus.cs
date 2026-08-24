using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RiverBox;

public static class SubmarineStatus
{
    public static unsafe long GetLatestReturnTime()
    {
        var territory = HousingManager.Instance()->WorkshopTerritory;
        if (territory == null)
            return 0;

        long latest = 0;
        var data = territory->Submersible.Data;
        for (int i = 0; i < data.Length; i++)
        {
            var sub = data[i];
            if (sub.RankId == 0)
                continue;
            if (sub.ReturnTime > latest)
                latest = sub.ReturnTime;
        }
        return latest;
    }

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

    public static string FormatTime(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return "-";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy/M/d HH:mm:ss");
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
}
