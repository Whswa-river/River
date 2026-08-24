using Dalamud.Game.Command;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;

namespace RiverBox;

public sealed class RiverBox : IDalamudPlugin
{
    public string Name => "RiverBox";

    private const string HelpMessage = "打开 RiverBox 界面";

    public static RiverBox P { get; private set; } = null!;
    public static RiverBoxConfig C { get; private set; } = null!;

    private readonly UIWindow _uiWindow;

    public RiverBox(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        ECommonsMain.Init(pluginInterface, this, Module.ObjectFunctions, Module.DalamudReflector);
        C = EzConfig.Init<RiverBoxConfig>();

        _uiWindow = new UIWindow();
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenConfigUi += Toggle;

        Svc.Commands.AddHandler("/riverbox", new CommandInfo(OnCommand) { HelpMessage = HelpMessage });
        Svc.Commands.AddHandler("/RB", new CommandInfo(OnCommand) { HelpMessage = HelpMessage });
        Svc.Commands.AddHandler("/RB mqs", new CommandInfo(OnCommand) { HelpMessage = "打开 快捷发宏 窗口" });
        Svc.Commands.AddHandler("/RB tp", new CommandInfo(OnCommand) { HelpMessage = "打开 随心而行 窗口" });
    }

    public void Dispose()
    {
        Svc.Commands.RemoveHandler("/riverbox");
        Svc.Commands.RemoveHandler("/RB");
        Svc.Commands.RemoveHandler("/RB mqs");
        Svc.Commands.RemoveHandler("/RB tp");

        _uiWindow.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();
        if (args.Equals("mqs", System.StringComparison.OrdinalIgnoreCase))
            _uiWindow.ToggleMacroFloating();
        else if (args.Equals("tp", System.StringComparison.OrdinalIgnoreCase))
            _uiWindow.ToggleTeleportFloating();
        else
            _uiWindow.Toggle();
    }

    private void Draw() => _uiWindow.Draw();

    private void Toggle() => _uiWindow.Toggle();
}
