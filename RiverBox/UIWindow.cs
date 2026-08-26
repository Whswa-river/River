using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using ECommons.Automation;
using ECommons.DalamudServices;
using Player = ECommons.GameHelpers.LegacyPlayer.Player;

namespace RiverBox;

public sealed class UIWindow : IDisposable
{
    private const string Title = "RiverBox";
    private const string Version = "v1.0.5.0";

    private static readonly Vector4 AccentColor = new(0f, 1f, 1f, 1f);          // 霓虹青
    private static readonly Vector4 PrimaryText = new(0.9f, 0.95f, 1f, 1f);      // 亮白偏蓝
    private static readonly Vector4 SecondaryText = new(0.5f, 0.7f, 0.8f, 0.8f); // 暗青
    private static readonly Vector4 WindowBg = new(0.03f, 0.02f, 0.06f, 0.97f);  // 深紫黑
    private static readonly Vector4 BetaColor = new(1f, 0.2f, 0.6f, 1f);         // 霓虹粉
    private static readonly Vector4 ActiveColor = new(0.2f, 1f, 0.4f, 1f);       // 霓虹绿
    private static readonly Vector4 BtnBgNormal = new(0.08f, 0.06f, 0.14f, 0.9f); // 暗紫
    private static readonly Vector4 NeonPurple = new(0.6f, 0.2f, 1f, 1f);        // 霓虹紫
    private static readonly Vector4 NeonPink = new(1f, 0.1f, 0.5f, 1f);          // 霓虹粉红
    private static readonly Vector4 BorderGlow = new(0f, 0.8f, 1f, 0.3f);        // 发光边框
    private static readonly Vector4 WarningColor = new(1f, 0.8f, 0.2f, 1f);      // 霓虹黄
    private static readonly Vector4 SuccessColor = new(0.2f, 1f, 0.4f, 1f);      // 霓虹绿

    private const float ColStatus = 280f;
    private const float ColBtn1 = 380f;
    private const float ColBtn2 = 440f;
    private const float ColBtn3 = 500f;
    private const float ColBtn4 = 560f;

    private const float InputX = 50f;
    private const float InputW = 110f;
    private const float LabelX2 = 200f;
    private const float InputX2 = 250f;

    private bool _visible = false;
    private bool _drawing;
    private int _selectedFeature = 0;
    private int _activeTab = 0;
    private string _renamingPlugin = "";
    private string _renameInput = "";
    private string _renamingGroup = "";
    private string _renamingGroupInput = "";
    private string _newGroupName = "";
    private string _pluginSearch = "";
    private string _newWorld = "";
    private string _newCharacter = "";

    private readonly MacroQuickSend _macroQuickSend;
    private readonly SubmarineCollect _submarineCollect;
    private readonly TeleportManager _teleportManager;

    private readonly string[] _tabs =
    {
        "日常", "插件管理", "自动化",
    };

    private readonly string[][] _featuresByTab =
    {
        new[] { "快捷发宏", "随心而行" },
        new[] { "插件管理" },
        new[] { "自动收艇" },
    };

    public UIWindow()
    {
        _macroQuickSend = new MacroQuickSend();
        _submarineCollect = new SubmarineCollect();
        _teleportManager = new TeleportManager();
    }

    public bool Visible => _visible;

    public void Toggle() => _visible = !_visible;

    public void ToggleMacroFloating() => _macroQuickSend.ToggleFloatingWindow();

    public void ToggleTeleportFloating() => _teleportManager.ToggleFloating();

    public void Draw()
    {
        if (_drawing)
            return;

        _drawing = true;
        try
        {
            if (_visible)
            {
                ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg);
                ImGui.PushStyleColor(ImGuiCol.Border, BorderGlow);
                ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
                ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.04f, 0.02f, 0.08f, 1f));
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.03f, 0.12f, 1f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.06f, 0.04f, 0.1f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.1f, 0.06f, 0.18f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.12f, 0.08f, 0.22f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.06f, 0.04f, 0.12f, 0.9f));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0f, 0.6f, 0.8f, 0.4f));
                ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0f, 0.4f, 0.6f, 0.5f));
                ImGui.PushStyleColor(ImGuiCol.Button, BtnBgNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.08f, 0.28f, 0.95f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.1f, 0.35f, 1f));
                ImGui.PushStyleColor(ImGuiCol.CheckMark, AccentColor);
                ImGui.PushStyleColor(ImGuiCol.SliderGrab, AccentColor);
                ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, NeonPurple);
                ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0f, 0.5f, 0.6f, 0.25f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0f, 0.6f, 0.7f, 0.35f));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0f, 0.7f, 0.8f, 0.4f));
                ImGui.PushStyleColor(ImGuiCol.Separator, BorderGlow);
                ImGui.PushStyleColor(ImGuiCol.Text, PrimaryText);

                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
                ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
                ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 1.2f * ImGui.GetFontSize()));

                try
                {
                    if (ImGui.Begin($"{Title}###RiverBoxMain", ref _visible, ImGuiWindowFlags.None))
                    {
                        ImGui.SetWindowSize(new Vector2(900, 600), ImGuiCond.FirstUseEver);
                        DrawHeader();
                        DrawTabBar();
                        DrawFooter();
                    }
                    ImGui.End();
                }
                finally
                {
                    ImGui.PopStyleVar(6);
                    ImGui.PopStyleColor(22);
                }
            }

            _teleportManager.DrawFloatingWindow();
        }
        finally
        {
            _drawing = false;
        }
    }

    private void DrawHeader()
    {
        var dl = ImGui.GetWindowDrawList();
        var titlePos = ImGui.GetCursorScreenPos();
        ImGui.TextColored(AccentColor, $"{Title} ");
        ImGui.SameLine();
        var verPos = ImGui.GetCursorScreenPos();
        ImGui.TextColored(NeonPurple, Version);

        dl.AddRectFilled(
            new Vector2(titlePos.X - 2, titlePos.Y - 1),
            new Vector2(verPos.X + 60, verPos.Y + ImGui.GetFontSize() + 1),
            ImGui.GetColorU32(new Vector4(0f, 1f, 1f, 0.04f)), 4f);
    }

    private void DrawTabBar()
    {
        ImGui.BeginTabBar("##RiverBoxTabs", ImGuiTabBarFlags.None);
        for (int i = 0; i < _tabs.Length; i++)
        {
            if (ImGui.BeginTabItem(_tabs[i], ImGuiTabItemFlags.None))
            {
                if (_activeTab != i)
                {
                    _activeTab = i;
                    _selectedFeature = 0;
                }
                DrawBody();
                ImGui.EndTabItem();
            }
        }
        ImGui.EndTabBar();
    }

    private void DrawBody()
    {
        float availY = ImGui.GetContentRegionAvail().Y;

        ImGui.BeginChild("##leftPanel", new Vector2(280, availY), true, ImGuiWindowFlags.None);
        DrawFeatureList();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##rightPanel", new Vector2(0, availY), true, ImGuiWindowFlags.None);
        DrawDetail();
        ImGui.EndChild();
    }

    private void DrawFeatureList()
    {
        var features = _featuresByTab[_activeTab];
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0f, 0.8f, 1f, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0f, 0.9f, 1f, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0f, 1f, 1f, 0.3f));

        for (int i = 0; i < features.Length; i++)
        {
            bool selected = i == _selectedFeature;

            if (ImGui.Selectable($"{features[i]}##{i}", selected))
            {
                _selectedFeature = i;
            }

            if (selected)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var dl = ImGui.GetWindowDrawList();
                dl.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), ImGui.GetColorU32(AccentColor));
                dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0f, 1f, 1f, 0.06f)));
            }
        }

        ImGui.PopStyleColor(3);
    }

    private void DrawDetail()
    {
        if (_activeTab == 0)
        {
            if (_selectedFeature == 0)
            {
                _macroQuickSend.DrawConfig();
                return;
            }

            if (_selectedFeature == 1)
            {
                _teleportManager.DrawConfig();
                return;
            }

            ImGui.TextColored(PrimaryText, _featuresByTab[0][_selectedFeature]);
            ImGui.TextColored(SecondaryText, "该功能待实现");
            return;
        }

        if (_activeTab == 1)
        {
            if (_selectedFeature == 0)
                DrawPluginPanel();
            return;
        }

        if (_activeTab == 2)
        {
            if (_selectedFeature == 0)
            {
                DrawSubmarinePanel();
                return;
            }
        }

        ImGui.TextColored(SecondaryText, "该标签待实现");
    }

    private void DrawSubmarinePanel()
    {
        ImGui.TextColored(PrimaryText, "自动收艇");
        ImGui.TextColored(SecondaryText, "勾选角色后点击开始，逐个切换角色并调用 DailyRoutines 收艇");
        ImGui.Separator();

        if (_submarineCollect.IsCollecting)
        {
            var progress = $"{_submarineCollect.StatusMessage} ({_submarineCollect.Config.CharacterPresets.Count})";
            ImGui.TextColored(ActiveColor, progress);

            if (ImGui.Button("停止", new Vector2(140, 0)))
                _submarineCollect.StopCollecting();
        }
        else
        {
            if (!string.IsNullOrEmpty(_submarineCollect.StatusMessage))
                ImGui.TextColored(SecondaryText, _submarineCollect.StatusMessage);

            bool anyPreset = _submarineCollect.Config.CharacterPresets.Count > 0;
            bool depsOk = IsDependenciesOk();
            if (!anyPreset || !depsOk)
                ImGui.BeginDisabled();

            if (ImGui.Button("开始收艇", new Vector2(140, 0)))
                _submarineCollect.StartCollecting();

            if (!anyPreset || !depsOk)
                ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("单角色收艇", new Vector2(140, 0)))
                Chat.ExecuteCommand("/pdr submarine");
        }

        ImGui.Separator();

        foreach (var preset in _submarineCollect.Config.CharacterPresets)
        {
            if (IsCurrentPlayer(preset))
            {
                var vessels = SubmarineStatus.GetVesselData();
                if (vessels.Count > 0)
                    _submarineCollect.Config.UpdateCache(preset.CharacterName, preset.WorldName, vessels);
            }
        }

        if (_submarineCollect.Config.CharacterPresets.Count == 0)
        {
            ImGui.TextColored(SecondaryText, "暂无预设，请在下方添加服务器和角色名");
        }

        for (int i = 0; i < _submarineCollect.Config.CharacterPresets.Count; i++)
        {
            var preset = _submarineCollect.Config.CharacterPresets[i];
            var cache = _submarineCollect.Config.GetCache(preset.CharacterName, preset.WorldName);
            bool isCurrent = IsCurrentPlayer(preset);

            string headerLabel = preset.FullName;
            if (isCurrent)
                headerLabel += " (当前)";

            if (cache != null && cache.Vessels.Count > 0)
            {
                var latestVessel = cache.Vessels.Where(v => v.ReturnTime > 0 && !v.IsCompleted)
                    .OrderByDescending(v => v.ReturnTime).FirstOrDefault();
                if (latestVessel != null)
                    headerLabel += $"  |  全部返航: {SubmarineStatus.FormatArrival(latestVessel.ReturnTime)}";
                else if (cache.Vessels.Any(v => v.IsCompleted))
                    headerLabel += "  |  全部完成";
            }

            ImGui.PushID(i);

            bool enabled = preset.Enabled;
            ImGui.PushStyleColor(ImGuiCol.CheckMark, AccentColor);
            if (ImGui.Checkbox($"##enabled_{i}", ref enabled))
            {
                preset.Enabled = enabled;
                _submarineCollect.Config.Save();
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();

            if (ImGui.CollapsingHeader($"{headerLabel}###sub_{i}"))
            {
                ImGui.Indent(10f);

                if (cache != null && cache.Vessels.Count > 0)
                {
                    if (ImGui.BeginTable($"##vesselTable_{i}", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn("艇名", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                        ImGui.TableSetupColumn("等级", ImGuiTableColumnFlags.WidthStretch, 0.15f);
                        ImGui.TableSetupColumn("返航时间", ImGuiTableColumnFlags.WidthStretch, 0.35f);
                        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                        ImGui.TableHeadersRow();

                        foreach (var vessel in cache.Vessels)
                        {
                            ImGui.TableNextRow();

                            ImGui.TableNextColumn();
                            ImGui.TextColored(PrimaryText, vessel.Name);

                            ImGui.TableNextColumn();
                            ImGui.TextColored(SecondaryText, $"Lv.{vessel.Level}");

                            ImGui.TableNextColumn();
                            if (vessel.ReturnTime > 0)
                            {
                                if (vessel.IsCompleted)
                                    ImGui.TextColored(SuccessColor, "已完成");
                                else
                                    ImGui.TextColored(WarningColor, SubmarineStatus.FormatRemaining(vessel.ReturnTime));
                            }
                            else
                            {
                                ImGui.TextColored(SecondaryText, "无航程");
                            }

                            ImGui.TableNextColumn();
                            if (vessel.IsCompleted)
                                ImGui.TextColored(SuccessColor, "待收取");
                            else if (vessel.ReturnTime > 0)
                                ImGui.TextColored(WarningColor, "远航中");
                            else
                                ImGui.TextColored(SecondaryText, "空闲");
                        }

                        ImGui.EndTable();
                    }

                    ImGui.TextColored(SecondaryText, $"最后更新: {cache.LastUpdateTime:yyyy-MM-dd HH:mm}");
                }
                else
                {
                    ImGui.TextColored(SecondaryText, "暂无数据，收艇后自动填充");
                }

                if (ImGui.SmallButton("删除"))
                {
                    _submarineCollect.Config.RemovePreset(preset.CharacterName, preset.WorldName);
                    i--;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("切换"))
                {
                    Chat.ExecuteCommand($"/ays relog {preset.CharacterName}@{preset.WorldName}");
                }

                ImGui.Unindent(10f);
            }

            ImGui.PopID();
        }

        ImGui.Separator();

        ImGui.TextColored(SecondaryText, "服务器");
        ImGui.SameLine(InputX);
        ImGui.SetNextItemWidth(InputW);
        ImGui.InputText("##newWorld", ref _newWorld, 32);
        ImGui.SameLine(LabelX2);
        ImGui.TextColored(SecondaryText, "角色");
        ImGui.SameLine(InputX2);
        ImGui.SetNextItemWidth(InputW);
        ImGui.InputText("##newChar", ref _newCharacter, 32);
        ImGui.SameLine();
        if (ImGui.Button("添加预设") && !string.IsNullOrWhiteSpace(_newWorld) && !string.IsNullOrWhiteSpace(_newCharacter))
        {
            _submarineCollect.Config.AddPreset(_newCharacter.Trim(), _newWorld.Trim());
            _newWorld = "";
            _newCharacter = "";
        }

        ImGui.SameLine();
        if (ImGui.Button("添加当前角色"))
        {
            _submarineCollect.AddCurrentCharacter();
        }

        ImGui.Separator();

        DrawDependencyStatus();
    }

    private static bool IsCurrentPlayer(CharacterPreset preset)
    {
        return Player.Available && Player.Name == preset.CharacterName && Player.HomeWorld == preset.WorldName;
    }

    private void DrawDependencyStatus()
    {
        bool lifeLoaded = IsPluginLoaded("Lifestream");
        bool drLoaded = IsPluginLoaded("DailyRoutines");

        ImGui.TextColored(lifeLoaded ? ActiveColor : BetaColor, $"Lifestream: {(lifeLoaded ? "已开启" : "未开启")}");
        ImGui.SameLine(160f);
        ImGui.TextColored(drLoaded ? ActiveColor : BetaColor, $"DailyRoutines: {(drLoaded ? "已开启" : "未开启")}");

        if (ImGui.SmallButton("加载 AutoSubmarineCollect"))
            Chat.ExecuteCommand("/pdr load AutoSubmarineCollect");
        ImGui.SameLine();
        if (ImGui.SmallButton("加载 AutoCutsceneSkip"))
            Chat.ExecuteCommand("/pdr load AutoCutsceneSkip");
        ImGui.SameLine();
        if (ImGui.SmallButton("加载 CallbackCommand"))
            Chat.ExecuteCommand("/pdr load CallbackCommand");
    }

    private static bool IsDependenciesOk() => IsPluginLoaded("Lifestream") && IsPluginLoaded("DailyRoutines");

    private void DrawPluginPanel()
    {
        ImGui.TextColored(PrimaryText, "插件管理");
        ImGui.TextColored(SecondaryText, "左键置顶 / 右键取消置顶 / 重命名仅本地显示");
        ImGui.Separator();

        List<PluginInfo> plugins = PluginManager.GetPlugins();
        if (plugins.Count == 0)
        {
            ImGui.TextColored(SecondaryText, "未找到插件");
            return;
        }

        ImGui.TextColored(SecondaryText, "新建分组");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        ImGui.InputText("##newGroup", ref _newGroupName, 32);
        ImGui.SameLine();
        if (ImGui.Button("创建") && !string.IsNullOrWhiteSpace(_newGroupName))
        {
            PluginManager.CreateGroup(_newGroupName.Trim());
            _newGroupName = "";
        }

        ImGui.SameLine();
        ImGui.TextColored(SecondaryText, "搜索");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        ImGui.InputText("##pluginSearch", ref _pluginSearch, 64);

        ImGui.Separator();

        var groups = PluginManager.GetGroupNames();
        if (!string.IsNullOrEmpty(_pluginSearch))
        {
            var search = _pluginSearch.ToLower();
            plugins = plugins.Where(p =>
                p.Name.ToLower().Contains(search) ||
                p.InternalName.ToLower().Contains(search)).ToList();
        }
        var ungrouped = plugins.Where(p => string.IsNullOrEmpty(PluginManager.GetPluginGroup(p.InternalName))).ToList();

        foreach (var group in groups)
        {
            var groupPlugins = plugins.Where(p => PluginManager.GetPluginGroup(p.InternalName) == group).ToList();
            if (groupPlugins.Count == 0 && ImGui.SmallButton($"删除##{group}"))
            {
                PluginManager.DeleteGroup(group);
                continue;
            }

            if (ImGui.CollapsingHeader($"{group} ({groupPlugins.Count})###group_{group}"))
            {
                ImGui.Indent(10f);
                foreach (var plugin in groupPlugins)
                    DrawPluginRow(plugin);
                if (ImGui.SmallButton($"移出全部##{group}"))
                {
                    foreach (var p in groupPlugins)
                        PluginManager.RemoveFromGroup(group, p.InternalName);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton($"启用全部##{group}"))
                {
                    foreach (var p in groupPlugins)
                        PluginManager.EnablePlugin(p.InternalName);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton($"禁用全部##{group}"))
                {
                    foreach (var p in groupPlugins)
                        PluginManager.DisablePlugin(p.InternalName);
                }
                ImGui.SameLine();
                if (ImGui.SmallButton($"重命名##{group}"))
                {
                    _renamingGroup = group;
                    _renamingGroupInput = group;
                }

                if (_renamingGroup == group)
                {
                    ImGui.SetNextItemWidth(150f);
                    if (ImGui.InputText("##renameGroup", ref _renamingGroupInput, 64, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        if (!string.IsNullOrWhiteSpace(_renamingGroupInput) && _renamingGroupInput != group)
                            PluginManager.RenameGroup(group, _renamingGroupInput.Trim());
                        _renamingGroup = "";
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("确认"))
                    {
                        if (!string.IsNullOrWhiteSpace(_renamingGroupInput) && _renamingGroupInput != group)
                            PluginManager.RenameGroup(group, _renamingGroupInput.Trim());
                        _renamingGroup = "";
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("取消"))
                    {
                        _renamingGroup = "";
                    }
                }

                ImGui.Unindent(10f);
            }
        }

        if (ungrouped.Count > 0)
        {
            if (ImGui.CollapsingHeader($"未分组 ({ungrouped.Count})###ungrouped"))
            {
                ImGui.Indent(10f);
                foreach (var plugin in ungrouped)
                    DrawPluginRow(plugin);
                ImGui.Unindent(10f);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(SecondaryText, "依赖插件: 无 (管理其他插件的启用/禁用/分组，仅依赖 Dalamud 本体)");
    }

    private void DrawPluginRow(PluginInfo plugin)
    {
        bool pinned = PluginManager.IsPinned(plugin.InternalName);
        string displayName = PluginManager.GetCustomName(plugin.InternalName);
        bool isRenaming = _renamingPlugin == plugin.InternalName;

        if (isRenaming)
        {
            ImGui.SetNextItemWidth(160f);
            if (ImGui.InputText("##rename", ref _renameInput, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                PluginManager.SetCustomName(plugin.InternalName, _renameInput.Trim());
                _renamingPlugin = "";
                _renameInput = "";
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("确认"))
            {
                PluginManager.SetCustomName(plugin.InternalName, _renameInput.Trim());
                _renamingPlugin = "";
                _renameInput = "";
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("取消"))
            {
                _renamingPlugin = "";
                _renameInput = "";
            }
        }
        else
        {
            ImGui.TextColored(plugin.IsLoaded ? ActiveColor : PrimaryText, displayName);
            if (displayName != plugin.InternalName)
            {
                ImGui.SameLine();
                ImGui.TextColored(SecondaryText, $"({plugin.InternalName})");
            }
        }

        ImGui.SameLine(ColStatus);
        ImGui.TextColored(SecondaryText, plugin.IsLoaded ? "已加载" : "未加载");

        if (!isRenaming)
        {
            ImGui.SameLine(ColBtn1);
            if (ImGui.SmallButton($"启用##{plugin.InternalName}"))
                PluginManager.EnablePlugin(plugin.InternalName);
            ImGui.SameLine(ColBtn2);
            if (ImGui.SmallButton($"禁用##{plugin.InternalName}"))
                PluginManager.DisablePlugin(plugin.InternalName);
            ImGui.SameLine(ColBtn3);
            if (pinned)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1f, 0.7f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.7f, 1f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.5f, 0.9f, 0.9f));
            }
            if (ImGui.SmallButton($"置顶##{plugin.InternalName}"))
                PluginManager.PinToTop(plugin.InternalName);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                PluginManager.Unpin(plugin.InternalName);
            if (pinned)
                ImGui.PopStyleColor(3);

            ImGui.SameLine(ColBtn4);
            if (ImGui.SmallButton($"重命名##{plugin.InternalName}"))
            {
                _renamingPlugin = plugin.InternalName;
                _renameInput = displayName;
            }

            string currentGroup = PluginManager.GetPluginGroup(plugin.InternalName);
            if (!string.IsNullOrEmpty(currentGroup))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"移出分组##{plugin.InternalName}"))
                    PluginManager.RemoveFromGroup(currentGroup, plugin.InternalName);
            }
            else
            {
                var groupNames = PluginManager.GetGroupNames();
                if (groupNames.Count > 0)
                {
                    ImGui.SameLine();
                    string comboId = $"##group_{plugin.InternalName}";
                    if (ImGui.BeginCombo(comboId, "加入分组", ImGuiComboFlags.HeightSmall))
                    {
                        foreach (var g in groupNames)
                        {
                            if (ImGui.Selectable($"{g}##add_{plugin.InternalName}_{g}"))
                                PluginManager.AddToGroup(g, plugin.InternalName);
                        }
                        ImGui.EndCombo();
                    }
                }
            }
        }

        ImGui.Separator();
    }

    private static bool IsPluginLoaded(string internalName)
    {
        return Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == internalName && x.IsLoaded);
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        ImGui.TextColored(SecondaryText, "使用说明 | 更新日志 | 反馈");

        string time = DateTime.Now.ToString("HH:mm:ss");
        float timeWidth = ImGui.CalcTextSize(time, false, 0f).X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - timeWidth);
        ImGui.TextColored(AccentColor, time);
    }

    public void Dispose()
    {
        _macroQuickSend?.Dispose();
        _submarineCollect?.Dispose();
        _teleportManager?.Dispose();
    }
}
