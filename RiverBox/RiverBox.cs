using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;

namespace RiverBox;

public sealed class RiverBox : IDalamudPlugin
{
    public string Name => "RiverBox";

    private const string HelpMessage = "打开 RiverBox 界面 | /rb mqs 快捷发宏 | /rb tpm 随心而行 | /rb mk [A-D/1-4] 场地标点 | /rb 收艇 开始收艇";

    public static RiverBox P { get; private set; } = null!;
    public static RiverBoxConfig C { get; private set; } = null!;

    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

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
        Svc.Commands.AddHandler("/rb", new CommandInfo(OnCommand) { HelpMessage = HelpMessage });
        Svc.Commands.AddHandler("/rb mqs", new CommandInfo(OnCommand) { HelpMessage = "打开 快捷发宏 窗口" });
        Svc.Commands.AddHandler("/rb tpm", new CommandInfo(OnCommand) { HelpMessage = "打开 随心而行 窗口" });
        Svc.Commands.AddHandler("/rb mk", new CommandInfo(OnCommand) { HelpMessage = "打开 场地标点 窗口" });
        Svc.Commands.AddHandler("/rb 收艇", new CommandInfo(OnCommand) { HelpMessage = "执行开始收艇" });
    }

    public void Dispose()
    {
        Svc.Commands.RemoveHandler("/riverbox");
        Svc.Commands.RemoveHandler("/rb");
        Svc.Commands.RemoveHandler("/rb mqs");
        Svc.Commands.RemoveHandler("/rb tpm");
        Svc.Commands.RemoveHandler("/rb mk");
        Svc.Commands.RemoveHandler("/rb 收艇");

        _uiWindow.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();
        if (args.Equals("mqs", System.StringComparison.OrdinalIgnoreCase))
            _uiWindow.ToggleMacroFloating();
        else if (args.Equals("tpm", System.StringComparison.OrdinalIgnoreCase))
            _uiWindow.ToggleTeleportFloating();
        else if (args.Equals("mk", System.StringComparison.OrdinalIgnoreCase))
            _uiWindow.ToggleFieldMarkerFloating();
        else if (args.StartsWith("mk ", System.StringComparison.OrdinalIgnoreCase))
        {
            var marker = args.Substring(3).Trim();
            _uiWindow.MoveToFieldMarker(marker);
        }
        else if (args.Equals("收艇", System.StringComparison.Ordinal))
            _uiWindow.StartSubmarineCollect();
        else
            _uiWindow.Toggle();
    }

    private void Draw() => _uiWindow.Draw();

    private void Toggle() => _uiWindow.Toggle();
}
