using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Player = ECommons.GameHelpers.LegacyPlayer.Player;

namespace RiverBox;

public class SubmarineCollect : IDisposable
{
    private SubmarineConfig _config;
    private bool _isCollecting = false;
    private int _currentPresetIndex = -1;
    private string _statusMessage = "";
    private DateTime _lastStatusUpdate = DateTime.MinValue;
    private Dictionary<string, SubmarineCharacterInfo> _characterInfo = [];

    private enum CollectState
    {
        Idle,
        Relogging,
        WaitingForCharacterReady,
        ExecutingPdr,
        WaitingAfterPdr,
        ClosingUI,
        Completed
    }

    private CollectState _currentState = CollectState.Idle;
    private Stopwatch _stateTimer = new();
    private string _currentCharacterName = "";
    private System.Diagnostics.Stopwatch _closingWatch = new();

    private const int PdrExecDelayMs = 5000;
    private const int WaitAfterPdrMs = 50000;

    private static readonly Vector4 AccentColor = new(0f, 1f, 1f, 1f);
    private static readonly Vector4 PrimaryText = new(0.9f, 0.95f, 1f, 1f);
    private static readonly Vector4 SecondaryText = new(0.5f, 0.7f, 0.8f, 0.8f);
    private static readonly Vector4 SuccessColor = new(0.2f, 1f, 0.4f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.3f, 0.3f, 1f);

    public bool IsCollecting => _isCollecting;
    public string StatusMessage => _statusMessage;
    public SubmarineConfig Config => _config;

    public SubmarineCollect()
    {
        _config = SubmarineConfig.Load();
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!_isCollecting || _currentState == CollectState.Idle)
            return;

        switch (_currentState)
        {
            case CollectState.Relogging:
                HandleReloggingState();
                break;
            case CollectState.WaitingForCharacterReady:
                HandleWaitingForCharacterReadyState();
                break;
            case CollectState.ExecutingPdr:
                HandleExecutingPdrState();
                break;
            case CollectState.WaitingAfterPdr:
                HandleWaitingAfterPdrState();
                break;
            case CollectState.ClosingUI:
                HandleClosingUIState();
                break;
        }
    }

    private void HandleReloggingState()
    {
        var preset = _config.CharacterPresets[_currentPresetIndex];
        if (IsOnTargetCharacter(preset))
        {
            UpdateStatus($"切换完成，等待角色就绪... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            _stateTimer.Restart();
            _currentState = CollectState.WaitingForCharacterReady;
            return;
        }

        if (_stateTimer.ElapsedMilliseconds > 60000)
        {
            UpdateStatus($"切换角色超时，跳过... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            SkipToNextCharacter();
        }
    }

    private void HandleWaitingForCharacterReadyState()
    {
        bool canMove = Player.Available
                       && !Svc.Condition[ConditionFlag.BetweenAreas]
                       && !Svc.Condition[ConditionFlag.BetweenAreas51];

        if (canMove)
        {
            UpdateStatus($"角色可移动，等待5秒... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            _stateTimer.Restart();
            _currentState = CollectState.ExecutingPdr;
        }
        else if (_stateTimer.ElapsedMilliseconds > 60000)
        {
            UpdateStatus($"等待角色就绪超时，跳过... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            SkipToNextCharacter();
        }
    }

    private void HandleExecutingPdrState()
    {
        if (_stateTimer.ElapsedMilliseconds >= PdrExecDelayMs)
        {
            UpdateStatus($"执行收艇指令... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            Chat.ExecuteCommand("/pdr submarine");
            _stateTimer.Restart();
            _currentState = CollectState.WaitingAfterPdr;
        }
    }

    private void HandleWaitingAfterPdrState()
    {
        var vessels = SubmarineStatus.GetVesselData();
        bool allVoyaging = vessels.Count > 0 && vessels.All(v => v.ReturnTime > 0 && !v.IsCompleted);

        if (allVoyaging && _stateTimer.ElapsedMilliseconds >= WaitAfterPdrMs)
        {
            UpdateStatus($"收艇完成，准备关闭界面... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            _stateTimer.Restart();
            _closingWatch.Restart();
            _currentState = CollectState.ClosingUI;
            return;
        }

        if (_stateTimer.ElapsedMilliseconds > 90000)
        {
            UpdateStatus($"等待收艇完成超时，跳过... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            SkipToNextCharacter();
        }
    }

    private void HandleClosingUIState()
    {
        if (_stateTimer.ElapsedMilliseconds < _config.DelayBetweenCallbacksMs)
            return;

        _stateTimer.Restart();

        // 整体超时兜底：如果长时间仍无法关闭窗口，强制关闭并继续
        if (_closingWatch.ElapsedMilliseconds > 40000)
        {
            UpdateStatus($"关闭窗口超时，强制关闭并继续... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            TryForceCloseWindow();
            CompleteCharacterCollect();
            return;
        }

        // 状态 32 (OccupiedInQuestEvent)：用回调指令 4 关闭
        if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            UpdateStatus($"关闭任务事件窗口 (回调4)... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            Chat.ExecuteCommand("/pdr callback SelectString 4");
            return;
        }

        // 状态 31 (OccupiedInEvent)：用回调指令 2 关闭
        if (Svc.Condition[ConditionFlag.OccupiedInEvent])
        {
            UpdateStatus($"关闭事件窗口 (回调2)... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            Chat.ExecuteCommand("/pdr callback SelectString 2");
            return;
        }

        // 状态 1 (NormalConditions)：窗口已关、角色可移动，切换角色
        if (!Svc.Condition[ConditionFlag.OccupiedInEvent] &&
            !Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            UpdateStatus($"窗口已关闭，切换下一角色... ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
            CompleteCharacterCollect();
        }
    }

    private void CompleteCharacterCollect()
    {
        var preset = _config.CharacterPresets[_currentPresetIndex];
        var vessels = SubmarineStatus.GetVesselData();
        if (vessels.Count > 0)
        {
            _config.UpdateCache(preset.CharacterName, preset.WorldName, vessels);
            UpdateStatus($"已缓存 {preset.FullName} 的 {vessels.Count} 艘潜艇数据 ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");
        }

        _stateTimer.Restart();
        _closingWatch.Reset();
        SkipToNextCharacter();
    }

    public void StartCollecting()
    {
        if (_isCollecting)
        {
            StopCollecting();
            return;
        }

        if (_config.CharacterPresets.Count == 0)
        {
            UpdateStatus("请先添加角色预设");
            return;
        }

        bool anyCompleted = false;
        foreach (var preset in _config.CharacterPresets)
        {
            var cache = _config.GetCache(preset.CharacterName, preset.WorldName);
            if (cache != null && cache.Vessels.Any(v => v.IsCompleted))
            {
                anyCompleted = true;
                break;
            }
        }

        if (!anyCompleted)
        {
            UpdateStatus("未有任何潜水艇返航");
            return;
        }

        _isCollecting = true;
        _currentPresetIndex = 0;
        _currentState = CollectState.Idle;
        _closingWatch.Reset();
        UpdateStatus("开始多角色收艇...");
        ProcessNextCharacter();
    }

    public void StopCollecting()
    {
        _isCollecting = false;
        _currentPresetIndex = -1;
        _currentState = CollectState.Idle;
        _stateTimer.Reset();
        _closingWatch.Reset();
        UpdateStatus("已停止收艇");
    }

    private void ProcessNextCharacter()
    {
        if (_currentPresetIndex >= _config.CharacterPresets.Count)
        {
            UpdateStatus("所有角色收艇完成");
            _isCollecting = false;
            _currentState = CollectState.Idle;
            return;
        }

        var preset = _config.CharacterPresets[_currentPresetIndex];
        _currentCharacterName = preset.FullName;

        if (!preset.Enabled)
        {
            UpdateStatus($"跳过 {preset.FullName} (已禁用)");
            _currentPresetIndex++;
            ProcessNextCharacter();
            return;
        }

        if (!HasReturnedSubmarine(preset))
        {
            UpdateStatus($"跳过 {preset.FullName} (无潜艇返回)");
            _currentPresetIndex++;
            ProcessNextCharacter();
            return;
        }

        if (IsOnTargetCharacter(preset))
        {
            UpdateStatus($"已在 {preset.FullName}，直接收艇");
            _stateTimer.Restart();
            _currentState = CollectState.ExecutingPdr;
            return;
        }

        UpdateStatus($"正在处理: {preset.FullName} ({_currentPresetIndex + 1}/{_config.CharacterPresets.Count})");

        var relogCommand = _config.GetRelogCommand(preset);
        Chat.ExecuteCommand(relogCommand);

        _stateTimer.Restart();
        _currentState = CollectState.Relogging;
    }

    private bool HasReturnedSubmarine(CharacterPreset preset)
    {
        if (IsOnTargetCharacter(preset))
        {
            var vessels = SubmarineStatus.GetVesselData();
            if (vessels.Count > 0)
                return vessels.Any(v => v.IsCompleted);
        }

        var cache = _config.GetCache(preset.CharacterName, preset.WorldName);
        return cache != null && cache.Vessels.Any(v => v.IsCompleted);
    }

    private static bool IsOnTargetCharacter(CharacterPreset preset)
    {
        if (!Player.Available)
            return false;
        if (Player.Name != preset.CharacterName)
            return false;
        if (Player.HomeWorld != preset.WorldName)
            return false;
        return true;
    }

    private void SkipToNextCharacter()
    {
        _currentPresetIndex++;
        _stateTimer.Reset();
        ProcessNextCharacter();
    }

    private void UpdateStatus(string message)
    {
        _statusMessage = message;
        _lastStatusUpdate = DateTime.Now;
        Svc.Log.Info($"[SubmarineCollect] {message}");
    }

    private static void TryForceCloseWindow()
    {
        try
        {
            unsafe
            {
                var addonPtr = Svc.GameGui.GetAddonByName("SelectString");
                if (addonPtr == default) return;
                var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addonPtr.Address;
                addon->Close(true);
            }
        }
        catch { }
    }

    public void AddCurrentCharacter()
    {
        var localPlayer = Player.Object;
        if (localPlayer != null)
        {
            var playerName = localPlayer.Name.ToString();
            var worldName = localPlayer.HomeWorld.Value.Name.ToString();
            _config.AddPreset(playerName, worldName);
            UpdateStatus($"已添加当前角色: {playerName}@{worldName}");
        }
        else
        {
            UpdateStatus("无法获取当前角色信息");
        }
    }

    public SubmarineCharacterInfo GetCharacterInfo(string characterName, string worldName)
    {
        var key = $"{characterName}@{worldName}";
        if (_characterInfo.TryGetValue(key, out var info))
            return info;

        return new SubmarineCharacterInfo
        {
            CharacterName = characterName,
            WorldName = worldName,
            Submarines = []
        };
    }

    public void UpdateCharacterSubmarineInfo(string characterName, string worldName, List<SubmarineData> submarines)
    {
        var key = $"{characterName}@{worldName}";
        _characterInfo[key] = new SubmarineCharacterInfo
        {
            CharacterName = characterName,
            WorldName = worldName,
            Submarines = submarines,
            LastUpdateTime = DateTime.Now
        };
    }

    public DateTime? GetLatestReturnTime(string characterName, string worldName)
    {
        var info = GetCharacterInfo(characterName, worldName);
        if (info.Submarines.Count == 0)
            return null;

        var completedSubs = info.Submarines.Where(s => !s.IsCompleted && s.ReturnTime.HasValue);
        if (!completedSubs.Any())
            return null;

        return completedSubs.Max(s => s.ReturnTime);
    }

    public int GetCompletedCount(string characterName, string worldName)
    {
        var info = GetCharacterInfo(characterName, worldName);
        return info.Submarines.Count(s => s.IsCompleted);
    }

    public void DrawConfig()
    {
        ImGui.TextColored(AccentColor, "多角色收艇功能");
        ImGui.Text("自动切换角色并执行收艇指令");
        ImGui.NewLine();

        var autoCollectEnabled = _config.AutoCollectEnabled;
        if (ImGui.Checkbox("启用自动收艇", ref autoCollectEnabled))
        {
            _config.AutoCollectEnabled = autoCollectEnabled;
            _config.Save();
        }

        ImGui.SameLine(200f);
        if (_isCollecting)
        {
            ImGui.TextColored(WarningColor, $"当前进度: {_currentPresetIndex + 1}/{_config.CharacterPresets.Count}");
        }

        ImGui.Separator();
        ImGui.TextColored(AccentColor, "延迟设置:");

        var delayAfterRelog = (float)_config.DelayAfterRelogMs / 1000f;
        if (ImGui.SliderFloat("角色切换后延迟(秒)", ref delayAfterRelog, 1f, 10f, "%.1f"))
        {
            _config.DelayAfterRelogMs = (int)(delayAfterRelog * 1000);
            _config.Save();
        }

        var delayBetweenCallbacks = (float)_config.DelayBetweenCallbacksMs / 1000f;
        if (ImGui.SliderFloat("关闭界面延迟(秒)", ref delayBetweenCallbacks, 0.1f, 2f, "%.1f"))
        {
            _config.DelayBetweenCallbacksMs = (int)(delayBetweenCallbacks * 1000);
            _config.Save();
        }

        var delayAfterCollect = (float)_config.DelayAfterCollectMs / 1000f;
        if (ImGui.SliderFloat("收艇后等待(秒)", ref delayAfterCollect, 1f, 10f, "%.1f"))
        {
            _config.DelayAfterCollectMs = (int)(delayAfterCollect * 1000);
            _config.Save();
        }

        ImGui.Separator();
        ImGui.TextColored(AccentColor, "角色预设:");

        if (ImGui.Button("添加当前角色"))
        {
            AddCurrentCharacter();
        }

        ImGui.SameLine();
        if (ImGui.Button("清空所有预设"))
        {
            _config.CharacterPresets.Clear();
            _config.Save();
        }

        ImGui.Separator();

        if (_config.CharacterPresets.Count > 0)
        {
            if (ImGui.BeginTable("##submarineTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("角色", ImGuiTableColumnFlags.WidthStretch, 0.30f);
                ImGui.TableSetupColumn("最晚返回", ImGuiTableColumnFlags.WidthStretch, 0.30f);
                ImGui.TableSetupColumn("完成", ImGuiTableColumnFlags.WidthStretch, 0.15f);
                ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthStretch, 0.15f);
                ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthStretch, 0.10f);
                ImGui.TableHeadersRow();

                for (int i = 0; i < _config.CharacterPresets.Count; i++)
                {
                    var preset = _config.CharacterPresets[i];
                    var info = GetCharacterInfo(preset.CharacterName, preset.WorldName);
                    var latestTime = GetLatestReturnTime(preset.CharacterName, preset.WorldName);
                    var completedCount = GetCompletedCount(preset.CharacterName, preset.WorldName);

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.TextColored(PrimaryText, preset.FullName);

                    ImGui.TableNextColumn();
                    if (latestTime.HasValue)
                    {
                        if (latestTime > DateTime.Now)
                        {
                            var remaining = latestTime.Value - DateTime.Now;
                            var totalHours = remaining.TotalHours;
                            var progress = 1f - (float)(totalHours / 24.0);
                            progress = Math.Clamp(progress, 0f, 1f);

                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.8f, 0.5f, 0.2f, 1f));
                            ImGui.ProgressBar(progress, new(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()), "");
                            ImGui.PopStyleColor();
                            ImGui.SameLine(0, 4);
                            ImGui.TextColored(WarningColor, $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}");
                        }
                        else
                        {
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, SuccessColor);
                            ImGui.ProgressBar(1f, new(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()), "");
                            ImGui.PopStyleColor();
                            ImGui.SameLine(0, 4);
                            ImGui.TextColored(SuccessColor, "已完成");
                        }
                    }
                    else if (info.Submarines.Count > 0 && completedCount > 0)
                    {
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, SuccessColor);
                        ImGui.ProgressBar(1f, new(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()), "");
                        ImGui.PopStyleColor();
                        ImGui.SameLine(0, 4);
                        ImGui.TextColored(SuccessColor, "全部完成");
                    }
                    else
                    {
                        ImGui.TextColored(SecondaryText, "无数据");
                    }

                    ImGui.TableNextColumn();
                    if (info.Submarines.Count > 0)
                    {
                        ImGui.Text($"{completedCount}/{info.Submarines.Count}");
                    }
                    else
                    {
                        ImGui.TextColored(SecondaryText, "-");
                    }

                    ImGui.TableNextColumn();
                    if (_isCollecting && _currentPresetIndex == i)
                    {
                        ImGui.TextColored(WarningColor, "处理中");
                    }
                    else if (completedCount > 0 && info.Submarines.Count > 0)
                    {
                        ImGui.TextColored(SuccessColor, "待收取");
                    }
                    else
                    {
                        ImGui.TextColored(SecondaryText, "远航中");
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"删除##{i}"))
                    {
                        _config.RemovePreset(preset.CharacterName, preset.WorldName);
                        i--;
                    }
                }

                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.TextColored(SecondaryText, "暂无角色预设，请添加角色");
        }

        ImGui.Separator();

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.TextColored(WarningColor, $"状态: {_statusMessage}");
        }

        if (_isCollecting)
        {
            if (ImGui.Button("停止收艇"))
            {
                StopCollecting();
            }
        }
        else
        {
            if (ImGui.Button("开始收艇"))
            {
                StartCollecting();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(SecondaryText, "依赖插件: AutoRetainer (/ays relog 切换角色) + DailyRoutines (/pdr submarine 收艇、/pdr callback 回调) + Lifestream (切换角色回工坊)");
    }
}

public class SubmarineCharacterInfo
{
    public string CharacterName { get; set; } = "";
    public string WorldName { get; set; } = "";
    public List<SubmarineData> Submarines { get; set; } = [];
    public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;
}

public class SubmarineData
{
    public int Index { get; set; } = 0;
    public uint VentureId { get; set; } = 0;
    public DateTime? ReturnTime { get; set; } = null;
    public bool IsCompleted => ReturnTime.HasValue && ReturnTime <= DateTime.Now;
}
