using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Player = ECommons.GameHelpers.LegacyPlayer.Player;

namespace RiverBox;

public sealed class FieldMarkerMover : IDisposable
{
    private static readonly string[] MarkerNames = ["A", "B", "C", "D", "1", "2", "3", "4"];

    private static readonly Vector4 AccentColor = new(0f, 1f, 1f, 1f);
    private static readonly Vector4 PrimaryText = new(0.9f, 0.95f, 1f, 1f);
    private static readonly Vector4 SecondaryText = new(0.5f, 0.7f, 0.8f, 0.8f);
    private static readonly Vector4 SuccessColor = new(0.2f, 1f, 0.4f, 1f);
    private static readonly Vector4 WarningColor = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 WindowBg = new(0.03f, 0.02f, 0.06f, 0.97f);
    private static readonly Vector4 BorderGlow = new(0f, 0.8f, 1f, 0.3f);
    private static readonly Vector4 BtnBgNormal = new(0.08f, 0.06f, 0.14f, 0.9f);
    private static readonly Vector4 NeonPurple = new(0.6f, 0.2f, 1f, 1f);

    private bool _isMoving;
    private int _selectedMarker = -1;
    private bool _floatingVisible;
    private bool _drawing;

    public bool FloatingVisible => _floatingVisible;

    public void Dispose()
    {
        Stop();
    }

    public void ToggleFloating() => _floatingVisible = !_floatingVisible;
    public void OpenFloating() => _floatingVisible = true;

    public void MoveTo(Vector3 pos)
    {
        Svc.Commands.ProcessCommand($"/vnav moveto {pos.X} {pos.Y} {pos.Z}");
        _isMoving = true;
    }

    public unsafe void MoveToByName(string name)
    {
        var controller = MarkingController.Instance();
        if (controller == null) return;

        var idx = -1;
        for (var i = 0; i < 8; i++)
        {
            if (MarkerNames[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0) return;

        var marker = controller->FieldMarkers[idx];
        if (!marker.Active) return;

        var pos = new Vector3(marker.X / 1000f, marker.Y / 1000f, marker.Z / 1000f);
        _selectedMarker = idx;
        MoveTo(pos);
    }

    public void Stop()
    {
        Svc.Commands.ProcessCommand("/vnav stop");
        _isMoving = false;
        _selectedMarker = -1;
    }

    public unsafe void DrawFloatingWindow()
    {
        if (!_floatingVisible || _drawing) return;
        _drawing = true;
        try
        {
            var controller = MarkingController.Instance();
            if (controller == null) return;

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
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));

            ImGui.Begin("##FieldMarkerFloating", ref _floatingVisible,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);

            ImGui.TextColored(AccentColor, "场地标点");
            ImGui.Separator();

            var playerPos = Player.Object?.Position ?? Vector3.Zero;
            var markers = new List<(int idx, string name, Vector3 pos, float dist)>();

            for (var i = 0; i < 8; i++)
            {
                var marker = controller->FieldMarkers[i];
                if (!marker.Active) continue;

                var pos = new Vector3(marker.X / 1000f, marker.Y / 1000f, marker.Z / 1000f);
                var dist = Vector3.Distance(playerPos, pos);
                markers.Add((i, MarkerNames[i], pos, dist));
            }

            markers.Sort((a, b) => a.dist.CompareTo(b.dist));

            if (markers.Count == 0)
            {
                ImGui.TextColored(SecondaryText, "无标点");
            }
            else
            {
                int perRow = 0;
                foreach (var m in markers)
                {
                    if (perRow > 0 && perRow % 4 == 0)
                        ImGui.NewLine();
                    if (perRow % 4 != 0)
                        ImGui.SameLine();

                    if (ImGui.Button($"{m.name}##f{m.idx}", new Vector2(50, 28)))
                    {
                        _selectedMarker = m.idx;
                        MoveTo(m.pos);
                    }

                    ImGui.SameLine();
                    ImGui.TextColored(SecondaryText, $"{m.dist:F1}m");
                    perRow++;
                }
            }

            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(9);
            ImGui.End();
        }
        finally { _drawing = false; }
    }

    public unsafe void Draw()
    {
        ImGui.TextColored(AccentColor, "场地标点传送");
        ImGui.TextColored(SecondaryText, "读取当前场地标点，使用 vnavmesh 导航到标点位置");
        ImGui.Separator();

        ImGui.Spacing();

        if (ImGui.Button(_floatingVisible ? "关闭悬浮窗" : "打开悬浮窗", new Vector2(150, 30)))
            _floatingVisible = !_floatingVisible;
        ImGui.SameLine();
        ImGui.TextColored(SecondaryText, "或使用 /rb mk");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var controller = MarkingController.Instance();
        if (controller == null)
        {
            ImGui.TextColored(SecondaryText, "无法读取标点数据");
            return;
        }

        for (var i = 0; i < 8; i++)
        {
            var marker = controller->FieldMarkers[i];
            var name = MarkerNames[i];
            var active = marker.Active;
            var pos = new Vector3(marker.X / 1000f, marker.Y / 1000f, marker.Z / 1000f);

            if (active)
            {
                var label = $"{name}##{i}";
                if (ImGui.Button(label, new Vector2(50, 0)))
                {
                    _selectedMarker = i;
                    MoveTo(pos);
                }

                ImGui.SameLine();
                ImGui.Text($"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button($"{name}##{i}", new Vector2(50, 0));
                ImGui.EndDisabled();

                ImGui.SameLine();
                ImGui.TextColored(SecondaryText, "(未放置)");
            }
        }

        ImGui.Separator();

        if (_isMoving)
        {
            ImGui.TextColored(SuccessColor, $"正在前往标点 {(_selectedMarker >= 0 ? MarkerNames[_selectedMarker] : "")}...");
            ImGui.SameLine();
            if (ImGui.Button("停止"))
                Stop();
        }
    }
}
