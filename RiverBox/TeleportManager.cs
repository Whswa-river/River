using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;

namespace RiverBox;

public class TeleportManager : IDisposable
{
    private TeleportConfig _config = new();
    private bool _drawing;
    private bool _floatingVisible;
    private int _editGroupIdx = -1;
    private int _editBtnIdx = -1;
    private TeleportButtonData _editBuf = new();
    private string _newGroupName = "";
    private bool _needOpenEditPopup;
    private string _importMessage = "";
    private DateTime _importMessageTime = DateTime.MinValue;

    // 传送等待改为 Framework.Update 驱动，避免在后台线程调用游戏主线程 API。
    private enum TeleportPhase { Idle, WaitingTerritory, WaitingReady, WaitingFinal }
    private TeleportPhase _tpPhase = TeleportPhase.Idle;
    private TeleportButtonData? _tpButton;
    private uint _tpTargetTerritory;
    private uint _tpOriginTerritory;
    private long _tpDeadline;
    private long _tpFinalAt;

    private static readonly Vector4 WindowBg = new(0.03f, 0.02f, 0.06f, 0.97f);
    private static readonly Vector4 AccentColor = new(0f, 1f, 1f, 1f);
    private static readonly Vector4 PrimaryText = new(0.9f, 0.95f, 1f, 1f);
    private static readonly Vector4 SecondaryText = new(0.5f, 0.7f, 0.8f, 0.8f);
    private static readonly Vector4 BtnBgNormal = new(0.08f, 0.06f, 0.14f, 0.9f);
    private static readonly Vector4 BorderGlow = new(0f, 0.8f, 1f, 0.3f);
    private static readonly Vector4 NeonPurple = new(0.6f, 0.2f, 1f, 1f);
    private static readonly Vector4 ActiveColor = new(0.2f, 1f, 0.4f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.8f, 0.2f, 1f);

    private static float _btnWidth;

    public bool Enabled => _config.ModuleEnabled;
    public TeleportConfig Config => _config;

    public TeleportManager()
    {
        _config = LoadConfig();
        EnsureSpecialSceneGroup();
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    private static readonly (string Code, string Name)[] SpecialScenes = new[]
    {
        ("bozja", "战线"),
        ("zadnor", "高原"),
        ("anemos", "风岛"),
        ("pagos", "冰岛"),
        ("pyros", "火岛"),
        ("hydatos", "水岛"),
        ("diadem", "云冠群岛"),
        ("island", "无人岛"),
        ("ardorum", "月球1"),
        ("phaenna", "月球2"),
        ("oizys", "月球3"),
        ("auxesia", "月球4"),
        ("OCS", "南岛"),
        ("ocn", "北岛"),
    }; 
    
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(uint hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private static void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!OpenClipboard(0)) return;

        try
        {
            if (!EmptyClipboard()) return;

            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (hMem == IntPtr.Zero) return;

            var dest = GlobalLock(hMem);
            if (dest == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return;
            }

            Marshal.Copy(bytes, 0, dest, bytes.Length);
            GlobalUnlock(hMem);

            // 成功后内存所有权移交系统，不能再 GlobalFree；失败才需要释放。
            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                GlobalFree(hMem);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static string GetClipboardText()
    {
        if (!OpenClipboard(0)) return "";
        try
        {
            var hMem = GetClipboardData(CF_UNICODETEXT);
            if (hMem == IntPtr.Zero) return "";
            var ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero) return "";
            var text = Marshal.PtrToStringUni(ptr);
            GlobalUnlock(hMem);
            return text ?? "";
        }
        finally
        {
            CloseClipboard();
        }
    }

    private void EnsureSpecialSceneGroup()
    {
        const string specialGroupName = "特殊场景";
        var group = _config.Groups.Find(g => g.Name == specialGroupName);
        if (group == null)
        {
            group = new TeleportGroup { Name = specialGroupName, IsSystem = true };
            foreach (var (code, name) in SpecialScenes)
            {
                group.Buttons.Add(new TeleportButtonData
                {
                    Name = name,
                    CustomCommand = $"/pdrfe {code}",
                    ShowInFloating = true
                });
            }
            _config.Groups.Insert(0, group);
            SaveConfig();
        }
        else if (!group.IsSystem)
        {
            group.IsSystem = true;
            SaveConfig();
        }
    }
    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        CancelPendingTeleport();
    }
    public void ToggleFloating() => _floatingVisible = !_floatingVisible;
    public void OpenFloating() => _floatingVisible = true;

    private static void EnsureBtnWidth()
    {
        if (_btnWidth <= 0)
            _btnWidth = ImGui.CalcTextSize("某某某某").X + 24f;
    }

    private static string PadLabel(string text, int maxChars = 4)
    {
        var strLen = 0;
        int charCount = 0;
        foreach (var c in text)
        {
            if (c > 0x7F) strLen += 2;
            else strLen += 1;
            charCount++;
            if (strLen >= maxChars * 2) break;
        }
        if (charCount < maxChars)
            return text + new string(' ', (maxChars - charCount) * 2);
        return text;
    }

    private static bool CenteredButton(string label, float width)
    {
        var textSize = ImGui.CalcTextSize(label);
        var pos = ImGui.GetCursorScreenPos();
        var height = textSize.Y + ImGui.GetStyle().FramePadding.Y * 2;
        var frameRounding = ImGui.GetStyle().FrameRounding;

        bool pressed = ImGui.InvisibleButton(label + "##btn", new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();
        bool held = ImGui.IsItemActive();

        var dl = ImGui.GetWindowDrawList();
        var col = ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        if (held) col = ImGui.GetColorU32(ImGuiCol.FrameBgActive);

        dl.AddRectFilled(pos, pos + new Vector2(width, height), col, frameRounding);

        var textPos = new Vector2(
            pos.X + (width - textSize.X) * 0.5f,
            pos.Y + (height - textSize.Y) * 0.5f
        );
        dl.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), label);

        return pressed;
    }

    private static string GetCurrentAetheryteName()
    {
        try
        {
            var territoryId = Svc.ClientState.TerritoryType;
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
            if (sheet != null)
            {
                var row = sheet.GetRow((uint)territoryId);
                var placeName = row.PlaceName.ValueNullable;
                if (placeName != null)
                    return placeName.Value.Name.ToString();
                return row.Name.ToString();
            }
        }
        catch { }
        return "";
    }

    private static Vector3 GetCurrentPos()
    {
        var p = Player.Object;
        return p != null ? p.Position : Vector3.Zero;
    }

    public void DrawFloatingWindow()
    {
        if (!_floatingVisible || _drawing) return;
        _drawing = true;
        var begun = false;
        try
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg);
            ImGui.PushStyleColor(ImGuiCol.Border, BorderGlow);
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.04f, 0.02f, 0.08f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.03f, 0.12f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, BtnBgNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.08f, 0.28f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.1f, 0.35f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, PrimaryText);
            ImGui.PushStyleColor(ImGuiCol.Separator, BorderGlow);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3, 2));

            ImGui.Begin("##TeleportFloating", ref _floatingVisible,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
            begun = true;

            ImGui.TextColored(AccentColor, "随心而行");
            ImGui.Separator();

            bool any = false;
            EnsureBtnWidth();
            const int FloatPerRow = 4;
            foreach (var group in _config.Groups)
            {
                var vis = group.Buttons.FindAll(b => b.ShowInFloating);
                if (vis.Count == 0) continue;
                any = true;
                ImGui.TextColored(NeonPurple, group.Name);

                int rowCount = 0;
                foreach (var btn in vis)
                {
                    if (rowCount > 0 && rowCount % FloatPerRow == 0)
                        ImGui.NewLine();
                    if (rowCount % FloatPerRow != 0)
                        ImGui.SameLine();
                    if (CenteredButton(btn.Name, _btnWidth))
                        ExecuteTeleport(btn);
                    rowCount++;
                }
                ImGui.Spacing();
            }
            if (!any) ImGui.TextColored(SecondaryText, "无显示按钮");
        }
        finally
        {
            if (begun)
                ImGui.End();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(10); // 与上方 10 个 PushStyleColor 配对（原为 9，每帧泄漏 1 个）
            _drawing = false;
        }
    }

    public void DrawConfig()
    {
        ImGui.TextColored(AccentColor, "随心而行");
        ImGui.TextColored(SecondaryText, "自定义传送按钮，右键编辑定义地图水晶和坐标，实现一键传送TP到位");
        ImGui.Spacing();

        bool enabled = _config.ModuleEnabled;
        if (ImGui.Checkbox("启用模块", ref enabled))
        { _config.ModuleEnabled = enabled; SaveConfig(); }
        ImGui.SameLine();
        ImGui.TextColored(_config.ModuleEnabled ? ActiveColor : SecondaryText,
            _config.ModuleEnabled ? "[已启用]" : "[已禁用]");

        if (!_config.ModuleEnabled)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("打开传送界面"))
            _floatingVisible = !_floatingVisible;
        ImGui.SameLine();
        ImGui.TextColored(SecondaryText, "或使用 /RB tp");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawGroupEditor();

        if (_needOpenEditPopup)
        {
            _needOpenEditPopup = false;
            ImGui.OpenPopup("编辑按钮");
        }

        DrawEditPopup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200f);
        ImGui.InputText("##NewGN", ref _newGroupName, 64);
        ImGui.SameLine();
        if (ImGui.Button("添加分组") && !string.IsNullOrWhiteSpace(_newGroupName))
        {
            _config.Groups.Add(new TeleportGroup { Name = _newGroupName });
            _newGroupName = "";
            SaveConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(SecondaryText, "依赖插件: DailyRoutines (BetterTeleport 模块，需加载 /pdrtelepo 与 /pdrtp 指令)");
    }

    private void DrawGroupEditor()
    {
        for (int g = 0; g < _config.Groups.Count; g++)
        {
            var group = _config.Groups[g];

            ImGui.PushID($"group_{g}");

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.15f, 0.05f, 0.25f, 0.8f));
            if (ImGui.CollapsingHeader($"{group.Name}##{g}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.PopStyleColor(1);

                ImGui.Indent(10f);

                if (group.IsSystem)
                {
                    ImGui.TextColored(SecondaryText, "分组名:");
                    ImGui.SameLine();
                    ImGui.TextColored(NeonPurple, $"{group.Name} [系统分组]");
                }
                else
                {
                    ImGui.TextColored(SecondaryText, "分组名:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150f);
                    var grpName = group.Name;
                    if (ImGui.InputText($"##gn{g}", ref grpName, 64))
                        group.Name = grpName;

                    ImGui.SameLine();
                    if (ImGui.SmallButton("导出分组"))
                    {
                        ExportGroup(group);
                    }

                    ImGui.SameLine();
                    if (ImGui.SmallButton("导入分组"))
                    {
                        ImportGroup();
                    }

                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.1f, 0.1f, 0.8f));
                    var deleteGroup = ImGui.SmallButton("删除分组");
                    ImGui.PopStyleColor(1);
                    if (deleteGroup)
                    {
                        _config.Groups.RemoveAt(g);
                        SaveConfig();
                        // 提前 return 前补齐 ImGui 栈，否则每次删分组都会累积一次失衡。
                        ImGui.Unindent(10f);
                        ImGui.PopID();
                        return;
                    }

                    if (!string.IsNullOrEmpty(_importMessage) && (DateTime.Now - _importMessageTime).TotalSeconds < 2)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.2f, 1f, 0.4f, 1f), _importMessage);
                    }
                }

                ImGui.Separator();

                EnsureBtnWidth();
                const int MainPerRow = 10;

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));

                for (int b = 0; b < group.Buttons.Count; b++)
                {
                    var btn = group.Buttons[b];
                    ImGui.PushID($"btn_{g}_{b}");

                    var showTag = btn.ShowInFloating ? "*" : "";
                    var label = PadLabel($"{btn.Name}{showTag}");

                    if (b > 0 && b % MainPerRow == 0)
                        ImGui.NewLine();

                    if (b % MainPerRow != 0)
                        ImGui.SameLine();

                    if (CenteredButton(label, _btnWidth) && !group.IsSystem)
                    {
                        _editGroupIdx = g;
                        _editBtnIdx = b;
                        _editBuf = new TeleportButtonData
                        {
                            Name = btn.Name,
                            AetheryteName = btn.AetheryteName,
                            X = btn.X, Y = btn.Y, Z = btn.Z,
                            ShowInFloating = btn.ShowInFloating,
                            CustomCommand = btn.CustomCommand
                        };
                        _needOpenEditPopup = true;
                    }

                    if (ImGui.BeginPopupContextItem($"ctx_{g}_{b}"))
                    {
                        if (ImGui.MenuItem($"{(btn.ShowInFloating ? "隐藏" : "显示")}于浮窗"))
                        {
                            btn.ShowInFloating = !btn.ShowInFloating;
                            SaveConfig();
                        }
                        if (!group.IsSystem)
                        {
                            ImGui.Separator();
                            if (ImGui.MenuItem("编辑"))
                            {
                                _editGroupIdx = g;
                                _editBtnIdx = b;
                                _editBuf = new TeleportButtonData
                                {
                                    Name = btn.Name,
                                    AetheryteName = btn.AetheryteName,
                                    X = btn.X, Y = btn.Y, Z = btn.Z,
                                    ShowInFloating = btn.ShowInFloating,
                                    CustomCommand = btn.CustomCommand
                                };
                                _needOpenEditPopup = true;
                            }
                            if (ImGui.MenuItem("使用当前位置"))
                            {
                                btn.AetheryteName = GetCurrentAetheryteName();
                                var pos = GetCurrentPos();
                                btn.X = pos.X; btn.Y = pos.Y; btn.Z = pos.Z;
                                SaveConfig();
                            }
                            ImGui.Separator();
                            ImGui.PushStyleColor(ImGuiCol.Text, WarningColor);
                            var deleteButton = ImGui.MenuItem("删除");
                            ImGui.PopStyleColor(1);
                            if (deleteButton)
                            {
                                group.Buttons.RemoveAt(b);
                                SaveConfig();
                                // 提前 return 前补齐 ImGui 栈（StyleVar / Indent / 两级 ID）。
                                ImGui.EndPopup();
                                ImGui.PopID();
                                ImGui.PopStyleVar();
                                ImGui.Unindent(10f);
                                ImGui.PopID();
                                return;
                            }
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }

                ImGui.PopStyleVar();

                ImGui.Spacing();
                if (!group.IsSystem && ImGui.Button("+ 添加按钮"))
                {
                    group.Buttons.Add(new TeleportButtonData());
                    SaveConfig();
                }

                ImGui.Unindent(10f);
            }
            else
            {
                ImGui.PopStyleColor(1);
            }

            ImGui.PopID();
        }
    }

    private void DrawEditPopup()
    {
        if (!ImGui.BeginPopup("编辑按钮")) return;

        ImGui.TextColored(AccentColor, "编辑按钮");
        ImGui.Separator();

        ImGui.Text("名称:");
        ImGui.SetNextItemWidth(200f);
        var eName = _editBuf.Name;
        if (ImGui.InputText("##ename", ref eName, 64))
            _editBuf.Name = eName;

        ImGui.Text("水晶名:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160f);
        var eAeth = _editBuf.AetheryteName;
        if (ImGui.InputText("##eath", ref eAeth, 64))
            _editBuf.AetheryteName = eAeth;
        ImGui.SameLine();
        if (ImGui.Button("当前")) _editBuf.AetheryteName = GetCurrentAetheryteName();

        ImGui.Text("坐标 X:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var eX = _editBuf.X;
        if (ImGui.InputFloat("##ex", ref eX))
            _editBuf.X = eX;
        ImGui.SameLine();
        ImGui.Text("Y:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var eY = _editBuf.Y;
        if (ImGui.InputFloat("##ey", ref eY))
            _editBuf.Y = eY;
        ImGui.SameLine();
        ImGui.Text("Z:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        var eZ = _editBuf.Z;
        if (ImGui.InputFloat("##ez", ref eZ))
            _editBuf.Z = eZ;

        if (ImGui.Button("填充当前位置"))
        {
            _editBuf.AetheryteName = GetCurrentAetheryteName();
            var pos = GetCurrentPos();
            _editBuf.X = pos.X; _editBuf.Y = pos.Y; _editBuf.Z = pos.Z;
        }

        ImGui.Spacing();
        ImGui.Text("自定义命令 (留空则使用/tp+/pdrtp):");
        var eCmd = _editBuf.CustomCommand;
        if (ImGui.InputText("##ecmd", ref eCmd, 128))
            _editBuf.CustomCommand = eCmd;

        ImGui.Spacing();

        bool showFloat = _editBuf.ShowInFloating;
        if (ImGui.Checkbox("在浮窗显示", ref showFloat))
            _editBuf.ShowInFloating = showFloat;

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Button("保存") && _editGroupIdx >= 0 && _editBtnIdx >= 0)
        {
            var btn = _config.Groups[_editGroupIdx].Buttons[_editBtnIdx];
            btn.Name = _editBuf.Name;
            btn.AetheryteName = _editBuf.AetheryteName;
            btn.X = _editBuf.X; btn.Y = _editBuf.Y; btn.Z = _editBuf.Z;
            btn.ShowInFloating = _editBuf.ShowInFloating;
            btn.CustomCommand = _editBuf.CustomCommand;
            SaveConfig();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("取消"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void ExecuteTeleport(TeleportButtonData btn)
    {
        if (!string.IsNullOrEmpty(btn.CustomCommand))
        {
            Chat.ExecuteCommand(btn.CustomCommand);
            return;
        }

        if (string.IsNullOrEmpty(btn.AetheryteName)) return;

        var targetTerritory = GetTerritoryByAetheryteName(btn.AetheryteName);
        var currentTerritory = (uint)Svc.ClientState.TerritoryType;

        if (targetTerritory != 0 && currentTerritory == targetTerritory)
        {
            Chat.ExecuteCommand($"/pdrtp pos {btn.X} {btn.Y} {btn.Z}");
            return;
        }

        CancelPendingTeleport();
        _tpButton = btn;
        _tpTargetTerritory = targetTerritory;
        _tpOriginTerritory = currentTerritory;
        _tpDeadline = Environment.TickCount64 + 120000;
        _tpPhase = TeleportPhase.WaitingTerritory;
        Chat.ExecuteCommand($"/pdrtelepo {btn.AetheryteName}");
    }

    /// <summary>
    /// 原实现用 Task.Run + Thread.Sleep 轮询，并在后台线程调用 Chat.ExecuteCommand
    /// （内部直接调用游戏 UIModule.ProcessChatBoxEntry）和 Svc.Condition / Player，
    /// 这些 API 只能在游戏主线程使用。现改为 Framework.Update 驱动的状态机。
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_tpPhase == TeleportPhase.Idle || _tpButton == null)
            return;

        var now = Environment.TickCount64;
        switch (_tpPhase)
        {
            case TeleportPhase.WaitingTerritory:
            {
                var current = (uint)Svc.ClientState.TerritoryType;
                var arrived = _tpTargetTerritory != 0
                    ? current == _tpTargetTerritory
                    : current != _tpOriginTerritory;
                if (arrived)
                {
                    _tpDeadline = now + 60000;
                    _tpPhase = TeleportPhase.WaitingReady;
                }
                else if (now > _tpDeadline)
                {
                    Svc.Log.Warning("[TeleportManager] 等待切换地图超时，已取消后续坐标传送");
                    CancelPendingTeleport();
                }
                break;
            }
            case TeleportPhase.WaitingReady:
                if (Player.Available
                    && !Svc.Condition[ConditionFlag.BetweenAreas]
                    && !Svc.Condition[ConditionFlag.BetweenAreas51])
                {
                    _tpFinalAt = now + 500;
                    _tpPhase = TeleportPhase.WaitingFinal;
                }
                else if (now > _tpDeadline)
                {
                    Svc.Log.Warning("[TeleportManager] 等待角色就绪超时，已取消后续坐标传送");
                    CancelPendingTeleport();
                }
                break;
            case TeleportPhase.WaitingFinal:
                if (now < _tpFinalAt) break;
                var btn = _tpButton;
                CancelPendingTeleport();
                Chat.ExecuteCommand($"/pdrtp pos {btn!.X} {btn.Y} {btn.Z}");
                break;
        }
    }

    private void CancelPendingTeleport()
    {
        _tpPhase = TeleportPhase.Idle;
        _tpButton = null;
        _tpDeadline = 0;
        _tpFinalAt = 0;
    }

    private static uint GetTerritoryByAetheryteName(string name)
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
            if (sheet == null) return 0;
            foreach (var row in sheet)
            {
                var placeName = row.PlaceName.ValueNullable;
                if (placeName != null && placeName.Value.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return row.Territory.RowId;

                var aethernetName = row.AethernetName.ValueNullable;
                if (aethernetName != null && aethernetName.Value.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return row.Territory.RowId;
            }
        }
        catch { }
        return 0;
    }

    private static TeleportConfig LoadConfig()
    {
        try
        {
            var path = Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "TeleportManager.json");
            if (File.Exists(path))
                return System.Text.Json.JsonSerializer.Deserialize<TeleportConfig>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        return new();
    }

    private void SaveConfig()
    {
        try
        {
            var path = Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "TeleportManager.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(_config));
        }
        catch { }
    }

    private void SetImportMessage(string msg)
    {
        _importMessage = msg;
        _importMessageTime = DateTime.Now;
    }

    private void ExportGroup(TeleportGroup group)
    {
        try
        {
            var export = new TeleportGroup
            {
                Name = group.Name,
                Buttons = group.Buttons.Select(btn => new TeleportButtonData
                {
                    Name = btn.Name,
                    AetheryteName = btn.AetheryteName,
                    X = btn.X, Y = btn.Y, Z = btn.Z,
                    ShowInFloating = btn.ShowInFloating,
                    CustomCommand = btn.CustomCommand
                }).ToList()
            };
            var json = System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            SetClipboardText(json);
            SetImportMessage("已复制到剪贴板");
        }
        catch (Exception ex)
        {
            SetImportMessage($"导出失败: {ex.Message}");
        }
    }

    private void ImportGroup()
    {
        try
        {
            var clipboard = GetClipboardText();
            if (string.IsNullOrWhiteSpace(clipboard))
            {
                SetImportMessage("剪贴板为空");
                return;
            }

            var imported = System.Text.Json.JsonSerializer.Deserialize<TeleportGroup>(clipboard);
            if (imported == null || imported.Buttons == null || imported.Buttons.Count == 0)
            {
                SetImportMessage("格式错误或数据为空");
                return;
            }

            var newGroup = new TeleportGroup
            {
                Name = string.IsNullOrWhiteSpace(imported.Name) ? $"新分组{_config.Groups.Count + 1}" : imported.Name,
                IsSystem = false,
                Buttons = imported.Buttons.Select(btn => new TeleportButtonData
                {
                    Name = btn.Name,
                    AetheryteName = btn.AetheryteName,
                    X = btn.X, Y = btn.Y, Z = btn.Z,
                    ShowInFloating = btn.ShowInFloating,
                    CustomCommand = btn.CustomCommand
                }).ToList()
            };

            _config.Groups.Add(newGroup);
            SaveConfig();
            SetImportMessage($"导入成功: {newGroup.Name} ({newGroup.Buttons.Count} 个按钮)");
        }
        catch (Exception ex)
        {
            SetImportMessage($"导入失败: {ex.Message}");
        }
    }
}
