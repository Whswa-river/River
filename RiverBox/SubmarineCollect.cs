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
