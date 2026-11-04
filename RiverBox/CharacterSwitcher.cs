using System;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using Player = ECommons.GameHelpers.LegacyPlayer.Player;

namespace RiverBox;

/// <summary>
/// 内置切角色：/logout → 回标题 → 打开选人 → 选世界 → 选角色 → 等登录。
/// 全程只依赖 Dalamud / FFXIVClientStructs，不再调用外部插件。
/// </summary>
public sealed class CharacterSwitcher : IDisposable
{
    public enum SwitchState
    {
        Idle,
        LoggingOut,
        ConfirmingLogout,
        WaitingTitle,
        OpeningCharacterSelect,
        WaitingCharacterList,
        SelectingWorld,
        SelectingCharacter,
        WaitingLogin,
        CrossDataCenter,
        Succeeded,
        Failed
    }

    private const string AddonTitleMenu = "_TitleMenu";
    private const string AddonCharaSelect = "_CharaSelectListMenu";
    private const string AddonSelectYesno = "SelectYesno";
    private const string AddonLobbyDkt = "LobbyDKT";

    private const int LogoutTimeoutMs = 20000;
    private const int ConfirmTimeoutMs = 15000;
    private const int TitleTimeoutMs = 30000;
    private const int OpenSelectTimeoutMs = 15000;
    private const int CharacterListTimeoutMs = 30000;
    private const int WorldSelectTimeoutMs = 12000;
    private const int CharacterSelectTimeoutMs = 15000;
    private const int LoginTimeoutMs = 180000;
    private const int CrossDcTimeoutMs = 180000;

    private SwitchState _state = SwitchState.Idle;
    private CharacterPreset? _target;
    private string _status = "";
    private string _error = "";

    private long _deadline;
    private long _nextActionAt;

    private ushort _targetWorldId;
    private ushort _selectWorldId;
    private uint _targetDcId;
    private ulong _targetContentId;
    private bool _actionDone;
    private bool _crossDcTried;
    private int _openAttempts;

    public SwitchState State => _state;

    public string Status => string.IsNullOrEmpty(_error) ? _status : _error;

    public string TargetName => _target?.FullName ?? "";

    public bool IsBusy => _state is not (SwitchState.Idle or SwitchState.Succeeded or SwitchState.Failed);

    public bool LastSucceeded => _state == SwitchState.Succeeded;

    public bool LastFailed => _state == SwitchState.Failed;

    public CharacterSwitcher()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }

    // ───────────────────────────── 对外接口 ─────────────────────────────

    /// <summary>开始切到指定角色。返回 false 表示没有启动（原因见 Status）。</summary>
    public bool SwitchTo(CharacterPreset preset)
    {
        if (IsBusy)
            return false;

        _error = "";

        if (!Svc.ClientState.IsLoggedIn)
        {
            Fail("当前未登录，无法切号");
            return false;
        }

        if (Svc.Condition[ConditionFlag.InCombat])
        {
            Fail("战斗中无法切号");
            return false;
        }

        var worldId = GetWorldId(preset.WorldName);
        if (worldId == 0)
        {
            Fail($"未知服务器: {preset.WorldName}");
            return false;
        }

        _target = preset;
        _targetWorldId = worldId;
        _targetDcId = GetDcIdByWorld(worldId);
        _targetContentId = 0;
        _selectWorldId = 0;
        _actionDone = false;
        _crossDcTried = false;
        _openAttempts = 0;

        if (IsOnTarget(preset))
        {
            _status = $"已在 {preset.FullName}";
            _state = SwitchState.Succeeded;
            Svc.Log.Info($"[CharacterSwitcher] {_status}");
            return true;
        }

        _status = $"正在退出到标题画面... ({preset.FullName})";
        Svc.Log.Info($"[CharacterSwitcher] 开始切号 -> {preset.FullName}");
        Chat.ExecuteCommand("/logout");
        Enter(SwitchState.LoggingOut, LogoutTimeoutMs);
        return true;
    }

    public void Cancel(string reason = "已取消")
    {
        if (_state == SwitchState.Idle)
            return;

        _state = SwitchState.Idle;
        _status = reason;
        _error = "";
        Svc.Log.Info($"[CharacterSwitcher] {reason}");
    }

    /// <summary>消费一次成功/失败状态，把状态机复位为 Idle。</summary>
    public void Acknowledge()
    {
        if (_state is SwitchState.Succeeded or SwitchState.Failed)
            _state = SwitchState.Idle;
    }

    // ───────────────────────────── 状态机 ─────────────────────────────

    private void OnUpdate(IFramework _)
    {
        if (!IsBusy)
            return;

        try
        {
            Tick();
        }
        catch (Exception e)
        {
            Svc.Log.Error($"[CharacterSwitcher] 状态机异常: {e}");
            Fail($"切号异常: {e.Message}");
        }
    }

    private void Tick()
    {
        var now = Environment.TickCount64;

        if (_deadline > 0 && now > _deadline)
        {
            OnTimeout();
            return;
        }

        switch (_state)
        {
            case SwitchState.LoggingOut:
                TickLoggingOut();
                break;
            case SwitchState.ConfirmingLogout:
                TickConfirmingLogout();
                break;
            case SwitchState.WaitingTitle:
                TickWaitingTitle();
                break;
            case SwitchState.OpeningCharacterSelect:
                TickOpeningCharacterSelect(now);
                break;
            case SwitchState.WaitingCharacterList:
                TickWaitingCharacterList();
                break;
            case SwitchState.SelectingWorld:
                TickSelectingWorld(now);
                break;
            case SwitchState.SelectingCharacter:
                TickSelectingCharacter();
                break;
            case SwitchState.WaitingLogin:
                TickWaitingLogin();
                break;
            case SwitchState.CrossDataCenter:
                TickCrossDataCenter(now);
                break;
        }
    }

    private void TickLoggingOut()
    {
        if (!Svc.ClientState.IsLoggedIn)
        {
            _status = "已退出，等待标题画面...";
            Enter(SwitchState.WaitingTitle, TitleTimeoutMs);
            return;
        }

        if (IsAddonReady(AddonSelectYesno))
        {
            Enter(SwitchState.ConfirmingLogout, ConfirmTimeoutMs);
            return;
        }

        if (IsAddonReady(AddonLobbyDkt))
            Enter(SwitchState.WaitingTitle, TitleTimeoutMs);
    }

    private unsafe void TickConfirmingLogout()
    {
        if (!Svc.ClientState.IsLoggedIn)
        {
            _status = "已退出，等待标题画面...";
            Enter(SwitchState.WaitingTitle, TitleTimeoutMs);
            return;
        }

        var addon = GetAddon(AddonSelectYesno);
        if (addon == null || !addon->IsReady)
            return;

        addon->FireCallbackInt(0); // 0 = 是
        _status = "已确认退出，等待标题画面...";
        Enter(SwitchState.WaitingTitle, TitleTimeoutMs);
    }

    private void TickWaitingTitle()
    {
        if (IsAddonReady(AddonCharaSelect))
        {
            Enter(SwitchState.WaitingCharacterList, CharacterListTimeoutMs);
            return;
        }

        if (_crossDcTried)
        {
            // 跨大区途中可能先出现 LobbyDKT / TitleConnect，不急着开选人
            if (IsAddonReady(AddonTitleMenu))
            {
                Enter(SwitchState.OpeningCharacterSelect, OpenSelectTimeoutMs);
                _nextActionAt = 0;
            }
            return;
        }

        if (IsAddonReady(AddonTitleMenu))
        {
            Enter(SwitchState.OpeningCharacterSelect, OpenSelectTimeoutMs);
            _nextActionAt = 0;
        }
    }

    private void TickOpeningCharacterSelect(long now)
    {
        if (IsAddonReady(AddonCharaSelect))
        {
            Enter(SwitchState.WaitingCharacterList, CharacterListTimeoutMs);
            return;
        }

        if (now < _nextActionAt)
            return;

        if (!IsAddonReady(AddonTitleMenu))
            return;

        if (_openAttempts >= 3)
            return; // 交给超时处理，避免反复触发把界面顶掉

        _openAttempts++;
        _status = "正在打开选人界面...";
        SendLobbyEvent(0, 4); // eventKind=0，首参 4 = OpenCharacterSelect
        _nextActionAt = now + 3000;
    }

    private unsafe void TickWaitingCharacterList()
    {
        var lobby = AgentLobby.Instance();
        if (lobby == null)
            return;

        ref var client = ref lobby->LobbyData.LobbyUIClient;
        if (client.CurrentDataCenterCharacters.Count == 0)
            return;

        // 跨大区刚回来时列表可能还停留在旧大区，此时不能急着判定"找不到"
        if (_crossDcTried && _targetDcId != 0 && GetLoadedDcId(lobby) != _targetDcId)
            return;

        for (var i = 0; i < client.CurrentDataCenterCharacters.Count; i++)
        {
            var entry = client.CurrentDataCenterCharacters[i];
            if (entry.HomeWorldId != _targetWorldId)
                continue;
            if (!string.Equals(entry.NameString, _target!.CharacterName, StringComparison.Ordinal))
                continue;

            _targetContentId = entry.ContentId;
            _selectWorldId = entry.LoginFlags == CharaSelectCharacterEntryLoginFlags.DCTraveling
                ? entry.CurrentWorldId
                : entry.HomeWorldId;

            _status = $"已找到 {_target.FullName}，正在选择世界...";
            _actionDone = false;
            _nextActionAt = 0;
            Enter(SwitchState.SelectingWorld, WorldSelectTimeoutMs);
            return;
        }

        // 当前大区找不到 → 尝试跨大区
        if (!_crossDcTried && TryCrossDataCenter())
        {
            Enter(SwitchState.CrossDataCenter, CrossDcTimeoutMs);
            return;
        }

        Fail(_crossDcTried
            ? $"跨大区后仍未找到角色 {_target!.FullName}"
            : $"角色 {_target!.FullName} 不在当前大区（可安装 DCTravelerX 支持跨大区）");
    }

    private unsafe void TickSelectingWorld(long now)
    {
        var lobby = AgentLobby.Instance();
        if (lobby != null && lobby->WorldId == _selectWorldId)
        {
            _status = "世界已选中，正在选择角色...";
            Enter(SwitchState.SelectingCharacter, CharacterSelectTimeoutMs);
            return;
        }

        if (now < _nextActionAt)
            return;

        _status = $"正在选择世界 {GetWorldName(_selectWorldId)} ...";
        SelectWorldById(_selectWorldId);
        _nextActionAt = now + 1800;
    }

    private unsafe void TickSelectingCharacter()
    {
        var lobby = AgentLobby.Instance();
        if (lobby == null)
            return;

        if (Svc.ClientState.IsLoggedIn)
        {
            Enter(SwitchState.WaitingLogin, LoginTimeoutMs);
            return;
        }

        if (_actionDone)
            return;

        var entries = lobby->LobbyData.CharaSelectEntries;
        for (var i = 0; i < entries.Count; i++)
        {
            var ptr = entries[i];
            if (ptr.Value == null)
                continue;
            if (ptr.Value->ContentId != _targetContentId)
                continue;

            _status = "正在登录目标角色...";
            SendLobbyEvent(0, 21, i); // 选中第 i 个角色
            SendLobbyEvent(3, 0);     // 确认
            _actionDone = true;
            return;
        }
    }

    private void TickWaitingLogin()
    {
        if (!Svc.ClientState.IsLoggedIn)
            return;

        if (!Player.Available)
            return;

        if (IsOnTarget(_target!))
        {
            _status = $"切号完成: {_target!.FullName}";
            _state = SwitchState.Succeeded;
            _error = "";
            Svc.Log.Info($"[CharacterSwitcher] {_status}");
            return;
        }

        Fail($"登录角色不匹配：当前 {Player.Name}@{Player.HomeWorld}，目标 {_target!.FullName}");
    }

    private unsafe void TickCrossDataCenter(long now)
    {
        if (now < _nextActionAt)
            return;

        var lobby = AgentLobby.Instance();
        if (lobby == null)
            return;

        ref var client = ref lobby->LobbyData.LobbyUIClient;
        if (client.CurrentDataCenterCharacters.Count == 0)
            return;

        if (_targetDcId != 0 && GetLoadedDcId(lobby) != _targetDcId)
            return; // 还在旧大区，继续等

        // 给客户端一点时间稳定，再重新走选人流程
        _status = "大区切换完成，重新选择角色...";
        _actionDone = false;
        Enter(SwitchState.WaitingCharacterList, 90000);
    }

    private void OnTimeout()
    {
        Fail(_state switch
        {
            SwitchState.LoggingOut => "退出登录超时",
            SwitchState.ConfirmingLogout => "确认退出超时",
            SwitchState.WaitingTitle => "等待标题画面超时",
            SwitchState.OpeningCharacterSelect => "打开选人界面超时",
            SwitchState.WaitingCharacterList => "选人列表加载超时",
            SwitchState.SelectingWorld => "选择世界超时",
            SwitchState.SelectingCharacter => "选择角色超时",
            SwitchState.WaitingLogin => "登录超时（可能卡在排队或网络异常）",
            SwitchState.CrossDataCenter => "跨大区切换超时",
            _ => "切号超时"
        });
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private void Enter(SwitchState state, int timeoutMs)
    {
        _state = state;
        _deadline = Environment.TickCount64 + timeoutMs;
        if (state == SwitchState.CrossDataCenter)
            _nextActionAt = Environment.TickCount64 + 3000;
        Svc.Log.Debug($"[CharacterSwitcher] -> {state} ({_status})");
    }

    private void Fail(string reason)
    {
        _state = SwitchState.Failed;
        _error = reason;
        _deadline = 0;
        Svc.Log.Warning($"[CharacterSwitcher] 失败: {reason}");
    }

    private static bool IsOnTarget(CharacterPreset preset)
    {
        return Player.Available
               && string.Equals(Player.Name, preset.CharacterName, StringComparison.Ordinal)
               && string.Equals(Player.HomeWorld, preset.WorldName, StringComparison.Ordinal);
    }

    private static unsafe AtkUnitBase* GetAddon(string name)
    {
        var ptr = Svc.GameGui.GetAddonByName(name);
        if (ptr == default)
            return null;

        return (AtkUnitBase*)ptr.Address;
    }

    private static unsafe bool IsAddonReady(string name)
    {
        var addon = GetAddon(name);
        return addon != null && addon->IsReady;
    }

    private static ushort GetWorldId(string worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
            return 0;

        var sheet = Svc.Data.GetExcelSheet<World>();
        if (sheet == null)
            return 0;

        foreach (var row in sheet)
        {
            if (row.RowId == 0)
                continue;
            if (string.Equals(row.Name.ToString(), worldName.Trim(), StringComparison.OrdinalIgnoreCase))
                return (ushort)row.RowId;
        }

        return 0;
    }

    private static string GetWorldName(ushort worldId)
    {
        var sheet = Svc.Data.GetExcelSheet<World>();
        if (sheet != null && sheet.TryGetRow(worldId, out var row))
            return row.Name.ToString();

        return worldId.ToString();
    }

    private static string GetDataCenterName(ushort worldId)
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<World>();
            if (sheet == null || !sheet.TryGetRow(worldId, out var row))
                return "";

            return row.DataCenter.Value.Name.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static uint GetDcIdByWorld(ushort worldId)
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<World>();
            if (sheet != null && sheet.TryGetRow(worldId, out var row))
                return row.DataCenter.RowId;
        }
        catch
        {
            // ignored
        }

        return 0;
    }

    private static unsafe uint GetLoadedDcId(AgentLobby* lobby)
    {
        ref var client = ref lobby->LobbyData.LobbyUIClient;
        if (client.CurrentDataCenterWorlds.Count == 0)
            return 0;

        return GetDcIdByWorld(client.CurrentDataCenterWorlds[0].Id);
    }

    /// <summary>
    /// 向 AgentLobby 发送事件。
    /// 打开选人 = (0, 4)；选角色 = (0, 21, index) 再 (3, 0)；选世界 = (0, 25, 0, index)。
    /// </summary>
    private static unsafe void SendLobbyEvent(ulong eventKind, params int[] args)
    {
        var lobby = AgentLobby.Instance();
        if (lobby == null)
            return;

        var agent = (AgentInterface*)lobby;
        var values = stackalloc AtkValue[Math.Max(args.Length, 1)];
        for (var i = 0; i < args.Length; i++)
        {
            values[i] = default;
            values[i].Type = AtkValueType.Int;
            values[i].Int = args[i];
        }

        var result = stackalloc AtkValue[1];
        agent->ReceiveEvent(result, values, (uint)args.Length, eventKind);
    }

    private static unsafe void SelectWorldById(ushort worldId)
    {
        var worldName = GetWorldName(worldId);
        if (string.IsNullOrEmpty(worldName))
            return;

        var stage = AtkStage.Instance();
        if (stage == null)
            return;

        var array = stage->GetStringArrayData(StringArrayType.CharaSelect);
        if (array == null)
            return;

        var size = array->Size;
        var raw = (byte**)array->StringArray;

        for (var i = 0; i < size; i++)
        {
            var p = raw[i];
            if (p == null)
                continue;

            var name = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(p, StringLength(p)));
            if (string.IsNullOrEmpty(name))
                continue;

            if (!name.Contains(worldName, StringComparison.OrdinalIgnoreCase))
                continue;

            SendLobbyEvent(0, 25, 0, i); // eventKind=0，25 = 选择世界
            return;
        }
    }

    private static unsafe int StringLength(byte* p)
    {
        var len = 0;
        while (p[len] != 0 && len < 512)
            len++;
        return len;
    }

    private bool TryCrossDataCenter()
    {
        if (_crossDcTried)
            return false;

        var dcName = GetDataCenterName(_targetWorldId);
        if (string.IsNullOrEmpty(dcName))
        {
            _status = "无法确定目标大区";
            return false;
        }

        try
        {
            var sub = Svc.PluginInterface.GetIpcSubscriber<string, Task<bool>>("DCTravelerX.SelectDCAndLogin");
            if (!sub.HasFunction)
            {
                _status = "未安装 DCTravelerX，无法跨大区";
                return false;
            }

            _crossDcTried = true;
            _status = $"正在切换大区: {dcName} ...";
            _ = sub.InvokeFunc(dcName);
            Svc.Log.Info($"[CharacterSwitcher] 请求跨大区 -> {dcName}");
            return true;
        }
        catch (Exception e)
        {
            Svc.Log.Warning($"[CharacterSwitcher] 跨大区调用失败: {e.Message}");
            return false;
        }
    }
}
