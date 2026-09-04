using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using ECommons.Automation;
using ECommons.DalamudServices;

namespace RiverBox;

public class MacroQuickSend : IDisposable
{
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
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private static void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        OpenClipboard(0);
        EmptyClipboard();
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        var dest = GlobalLock(hMem);
        Marshal.Copy(bytes, 0, dest, bytes.Length);
        GlobalUnlock(hMem);
        SetClipboardData(CF_UNICODETEXT, hMem);
        CloseClipboard();
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

    private static Config _config = new();
    private static bool _showFloating;
    private static bool _drawing;
    private static string _importMessage = "";
    private static DateTime _importMessageTime = DateTime.MinValue;

    private static readonly Vector4 WindowBg = new(0.03f, 0.02f, 0.06f, 0.97f);
    private static readonly Vector4 PrimaryText = new(0.9f, 0.95f, 1f, 1f);
    private static readonly Vector4 SecondaryText = new(0.5f, 0.7f, 0.8f, 0.8f);
    private static readonly Vector4 AccentColor = new(0f, 1f, 1f, 1f);
    private static readonly Vector4 NeonPurple = new(0.6f, 0.2f, 1f, 1f);
    private static readonly Vector4 NeonPink = new(1f, 0.1f, 0.5f, 1f);
    private static readonly Vector4 BtnBgNormal = new(0.08f, 0.06f, 0.14f, 0.9f);
    private static readonly Vector4 BorderGlow = new(0f, 0.8f, 1f, 0.3f);
    private static readonly Vector4 SuccessColor = new(0.2f, 1f, 0.4f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.8f, 0.2f, 1f);

    public bool Enabled => _config.ModuleEnabled;
    public bool ShowFloating => _showFloating;

    public MacroQuickSend()
    {
        _config = LoadConfig();
        Svc.PluginInterface.UiBuilder.Draw += OnDraw;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= OnDraw;
    }

    private static void OnDraw()
    {
        if (!_showFloating || _drawing) return;

        _drawing = true;
        try
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg);
            ImGui.PushStyleColor(ImGuiCol.Border, BorderGlow);
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.04f, 0.02f, 0.08f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.06f, 0.03f, 0.12f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, BtnBgNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.08f, 0.28f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.1f, 0.35f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, PrimaryText);
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.06f, 0.04f, 0.12f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0f, 0.6f, 0.8f, 0.4f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0f, 0.4f, 0.6f, 0.5f));

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);

            ImGui.Begin(
                "##MQS Floating",
                ref _showFloating,
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoScrollbar);

ImGui.TextColored(AccentColor, "快捷发宏");
            ImGui.Separator();

            for (int g = 0; g < _config.Groups.Count; g++)
            {
                var group = _config.Groups[g];
                if (group.Macros.Count == 0) continue;

                bool collapsed = _config.CollapsedGroupIndices.Contains(g);

                ImGui.PushStyleColor(ImGuiCol.Text, NeonPurple);
                if (collapsed)
                {
                    if (ImGui.Selectable($"[+] {group.Name}###grp_{g}", false, ImGuiSelectableFlags.None, new Vector2(0, 22)))
                        _config.CollapsedGroupIndices.Remove(g);
                }
                else
                {
                    if (ImGui.Selectable($"[-] {group.Name}###grp_{g}", false, ImGuiSelectableFlags.None, new Vector2(0, 22)))
                        _config.CollapsedGroupIndices.Add(g);
                }
                ImGui.PopStyleColor();

                if (collapsed) continue;

                ImGui.Indent(10f);

                const float btnWidth = 80f;
                const int cols = 3;

                for (int i = 0; i < group.Macros.Count; i++)
                {
                    if (i % cols != 0) ImGui.SameLine();

                    string name = string.IsNullOrWhiteSpace(group.Macros[i].Name)
                        ? $"[宏 {i + 1}]"
                        : group.Macros[i].Name;

                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.08f, 0.06f, 0.14f, 0.9f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0f, 0.8f, 1f, 0.3f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.2f, 1f, 0.5f));
                    ImGui.PushStyleColor(ImGuiCol.Text, AccentColor);

                    if (ImGui.Button(name, new Vector2(btnWidth, 0)))
                    {
                        SendMacro(group.Macros[i].Command);
                    }

                    ImGui.PopStyleColor(4);
                }

                ImGui.Unindent(10f);
                ImGui.Spacing();
            }

            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(10);
            ImGui.End();
        }
        finally
        {
            _drawing = false;
        }
    }

    public void ToggleFloatingWindow()
    {
        if (!_config.ModuleEnabled)
        {
            _showFloating = false;
            return;
        }
        _showFloating = !_showFloating;
    }

    public void DrawConfig()
    {
        ImGui.TextColored(AccentColor, "宏快捷发送");
        ImGui.TextColored(SecondaryText, "预设宏文本，一键发送，支持分组与导入导出");
        ImGui.Spacing();

        bool moduleEnabled = _config.ModuleEnabled;
        if (ImGui.Checkbox("启用模块", ref moduleEnabled))
        { _config.ModuleEnabled = moduleEnabled; SaveConfig(_config); }

        ImGui.SameLine();
        ImGui.TextColored(_config.ModuleEnabled ? SuccessColor : WarningColor,
            _config.ModuleEnabled ? "[已启用]" : "[已禁用]");

        if (!_config.ModuleEnabled)
            return;

        ImGui.Spacing();

        int lineDelay = _config.LineDelayMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("多行发送间隔(ms)", ref lineDelay, 0, 2000))
        {
            _config.LineDelayMs = lineDelay;
            SaveConfig(_config);
        }

        ImGui.Spacing();

        if (!_config.ModuleEnabled)
            ImGui.BeginDisabled();

        if (ImGui.Button(_showFloating ? "关闭悬浮窗" : "打开悬浮窗", new Vector2(150, 34)))
        {
            if (_config.ModuleEnabled)
                _showFloating = !_showFloating;
        }

        if (!_config.ModuleEnabled)
            ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("添加分组", new Vector2(100, 34)))
        {
            var count = _config.Groups.Count + 1;
            _config.Groups.Add(new MacroGroup { Name = $"分组{count}", Macros = new() });
            _config.SelectedGroup = _config.Groups.Count - 1;
            SaveConfig(_config);
        }

        ImGui.SameLine();

        if (ImGui.Button("导出当前分组", new Vector2(130, 34)))
            ExportGroup();

        ImGui.SameLine();

        if (ImGui.Button("导入到新分组", new Vector2(130, 34)))
            ImportNewGroup();

        if (!string.IsNullOrEmpty(_importMessage) && (DateTime.Now - _importMessageTime).TotalSeconds < 2)
        {
            ImGui.SameLine();
            ImGui.TextColored(SuccessColor, _importMessage);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_config.Groups.Count == 0)
        {
            ImGui.TextColored(NeonPink, "暂无分组，请点击「添加分组」");
            return;
        }

        if (_config.SelectedGroup >= _config.Groups.Count)
            _config.SelectedGroup = 0;

        var groupNames = _config.Groups.Select(g => string.IsNullOrWhiteSpace(g.Name) ? "未命名" : g.Name).ToArray();

        int selected = _config.SelectedGroup;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("当前分组", ref selected, groupNames, groupNames.Length))
        {
            _config.SelectedGroup = selected;
            SaveConfig(_config);
        }

        ImGui.SameLine();

        var currentGroup = _config.Groups[_config.SelectedGroup];
        string groupName = currentGroup.Name ?? "";
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputText("分组名", ref groupName, 50))
            currentGroup.Name = groupName;

        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(_config);

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.75f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.88f, 0.24f, 0.24f, 1f));

        if (ImGui.Button("删除分组", new Vector2(100, 34)) && _config.Groups.Count > 1)
        {
            _config.Groups.RemoveAt(_config.SelectedGroup);
            if (_config.SelectedGroup >= _config.Groups.Count)
                _config.SelectedGroup = _config.Groups.Count - 1;
            SaveConfig(_config);
        }

        ImGui.PopStyleColor(2);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Columns(2, "macro_columns", false);
        ImGui.SetColumnWidth(0, 150);
        ImGui.TextColored(SecondaryText, "宏命名");
        ImGui.NextColumn();
        ImGui.TextColored(SecondaryText, "宏指令");
        ImGui.Columns(1);

        ImGui.Spacing();

        var macros = currentGroup.Macros;
        int removeIndex = -1;

        for (int i = 0; i < macros.Count; i++)
        {
            var entry = macros[i];
            ImGui.PushID(i);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.04f, 0.08f, 0.55f));
            ImGui.BeginChild($"macro_row_{i}", new Vector2(0, 82), false);

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 8));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.08f, 0.06f, 0.14f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.1f, 0.06f, 0.18f, 0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.12f, 0.08f, 0.22f, 0.9f));

            string name = entry.Name ?? "";
            ImGui.SetCursorPosY(10);
            ImGui.SetNextItemWidth(110);
            if (ImGui.InputText("##name", ref name, 100))
                entry.Name = name;
            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(_config);

            ImGui.SameLine();

            string cmd = entry.Command ?? "";
            ImGui.SetNextItemWidth(420);
            if (ImGui.InputTextMultiline("##cmd", ref cmd, 500, new Vector2(420, 52)))
                entry.Command = cmd;
            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(_config);

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(2);

            ImGui.SameLine();

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
            ImGui.PushStyleColor(ImGuiCol.Button, NeonPurple);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.3f, 1f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.15f, 0.9f, 1f));

            if (ImGui.Button("发送", new Vector2(55, 32)))
                SendMacro(entry.Command);

            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.75f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.88f, 0.24f, 0.24f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.14f, 0.14f, 1f));

            if (ImGui.Button("删除", new Vector2(55, 32)))
                removeIndex = i;

            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            bool canMoveUp = i > 0;
            bool canMoveDown = i < macros.Count - 1;

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.12f, 0.25f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.15f, 0.35f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.12f, 0.1f, 0.2f, 1f));

            if (ImGui.Button("↑", new Vector2(32, 32)) && canMoveUp)
            {
                (macros[i], macros[i - 1]) = (macros[i - 1], macros[i]);
                SaveConfig(_config);
            }

            ImGui.SameLine();

            if (ImGui.Button("↓", new Vector2(32, 32)) && canMoveDown)
            {
                (macros[i], macros[i + 1]) = (macros[i + 1], macros[i]);
                SaveConfig(_config);
            }

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();

            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            macros.RemoveAt(removeIndex);
            SaveConfig(_config);
        }

        ImGui.Spacing();

        if (ImGui.Button("添加宏", new Vector2(100, 34)))
        {
            macros.Add(new MacroEntry());
            SaveConfig(_config);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(SecondaryText, "依赖插件: 无 (使用游戏内置聊天指令，无需额外插件)");
    }

    private static void SendMacro(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var lines = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0) return;

        var delayMs = _config.LineDelayMs;

        if (lines.Count == 1)
        {
            Svc.Framework.RunOnTick(() => Chat.SendMessage(lines[0]));
            return;
        }

        Task.Run(async () =>
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                Svc.Framework.RunOnTick(() => Chat.SendMessage(line));
                if (i < lines.Count - 1 && delayMs > 0)
                    await Task.Delay(delayMs);
            }
        });
    }

    private void ExportGroup()
    {
        try
        {
            if (_config.SelectedGroup < 0 || _config.SelectedGroup >= _config.Groups.Count)
            {
                _importMessage = "请先选择分组";
                _importMessageTime = DateTime.Now;
                return;
            }

            var group = _config.Groups[_config.SelectedGroup];
            var export = new MacroGroup
            {
                Name = group.Name,
                Macros = group.Macros.Select(m => new MacroEntry { Name = m.Name, Command = m.Command }).ToList()
            };
            var json = System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            SetClipboardText(json);
            _importMessage = "已复制到剪贴板";
            _importMessageTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            _importMessage = $"导出失败: {ex.Message}";
            _importMessageTime = DateTime.Now;
        }
    }

    private void ImportNewGroup()
    {
        try
        {
            var clipboard = GetClipboardText();
            if (string.IsNullOrWhiteSpace(clipboard))
            {
                _importMessage = "剪贴板为空";
                _importMessageTime = DateTime.Now;
                return;
            }

            var imported = System.Text.Json.JsonSerializer.Deserialize<MacroGroup>(clipboard);
            if (imported == null || imported.Macros == null || imported.Macros.Count == 0)
            {
                _importMessage = "格式错误或数据为空";
                _importMessageTime = DateTime.Now;
                return;
            }

            var count = _config.Groups.Count + 1;
            var newGroup = new MacroGroup
            {
                Name = imported.Name ?? $"分组{count}",
                Macros = imported.Macros.Select(m => new MacroEntry { Name = m.Name, Command = m.Command }).ToList()
            };

            _config.Groups.Add(newGroup);
            _config.SelectedGroup = _config.Groups.Count - 1;
            SaveConfig(_config);
            _importMessage = $"导入成功: {newGroup.Name} ({newGroup.Macros.Count} 个宏)";
            _importMessageTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            _importMessage = $"导入失败: {ex.Message}";
            _importMessageTime = DateTime.Now;
        }
    }

    private static Config LoadConfig()
    {
        try
        {
            var path = System.IO.Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "MacroQuickSend.json");
            if (System.IO.File.Exists(path))
                return System.Text.Json.JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(path)) ?? new Config();
        }
        catch { }
        return new Config();
    }

    private static void SaveConfig(Config config)
    {
        try
        {
            var path = System.IO.Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "MacroQuickSend.json");
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(config));
        }
        catch { }
    }

    public class MacroEntry
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
    }

    public class MacroGroup
    {
        public string Name { get; set; } = "";
        public List<MacroEntry> Macros { get; set; } = new();
    }

    private class Config
    {
        public bool ModuleEnabled { get; set; } = false;
        public List<MacroGroup> Groups { get; set; } = new();
        public int SelectedGroup { get; set; } = 0;
        public int LineDelayMs { get; set; } = 300;
        public HashSet<int> CollapsedGroupIndices { get; set; } = new();
    }
}
