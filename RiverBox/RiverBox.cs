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

    private const string HelpMessage = "打开 RiverBox 界面 | /rb mqs 快捷发宏 | /rb tpm 随心而行 | /rb mk [A-D/1-4] 场地标点 | /rb 收艇 开始收艇 | /rb 切号 角色@服务器";

    public static RiverBox P { get; private set; } = null!;
    public static RiverBoxConfig C { get; private set; } = null!;

    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly UIWindow _uiWindow;

    public RiverBox(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        P = this;
        ECommonsMain.Init(pluginInterface, this, Module.ObjectFunctions, Module.DalamudReflector);
        C = EzConfig.Init<RiverBoxConfig>();
        // 旧配置文件里若为 null，反序列化会覆盖字段默认值。
        C.PluginCustomNames ??= [];
        C.PluginGroups ??= [];
        C.PluginPinnedOrder ??= [];
        C.FieldMarkerTpCommand ??= "";

        _uiWindow = new UIWindow();
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenConfigUi += Toggle;

        Svc.Commands.AddHandler("/riverbox", new CommandInfo(OnCommand) { HelpMessage = HelpMessage });
        Svc.Commands.AddHandler("/rb", new CommandInfo(OnCommand) { HelpMessage = HelpMessage });
        Svc.Commands.AddHandler("/rb mqs", new CommandInfo(OnCommand) { HelpMessage = "打开 快捷发宏 窗口" });
        Svc.Commands.AddHandler("/rb tpm", new CommandInfo(OnCommand) { HelpMessage = "打开 随心而行 窗口" });
        Svc.Commands.AddHandler("/rb mk", new CommandInfo(OnCommand) { HelpMessage = "打开 场地标点 窗口" });
        Svc.Commands.AddHandler("/rb 收艇", new CommandInfo(OnCommand) { HelpMessage = "执行开始收艇" });
        Svc.Commands.AddHandler("/rb 切号", new CommandInfo(OnCommand) { HelpMessage = "切换到指定角色（角色名@服务器名）" });
    }

    public void Dispose()
    {
        Svc.Commands.RemoveHandler("/riverbox");
        Svc.Commands.RemoveHandler("/rb");
        Svc.Commands.RemoveHandler("/rb mqs");
        Svc.Commands.RemoveHandler("/rb tpm");
        Svc.Commands.RemoveHandler("/rb mk");
        Svc.Commands.RemoveHandler("/rb 收艇");
        Svc.Commands.RemoveHandler("/rb 切号");

        // 必须解绑，否则插件卸载/重载后 Dalamud 仍持有旧实例的两个委托，
        // 继续调用已 Dispose 的 UIWindow。
        _pluginInterface.UiBuilder.Draw -= Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= Toggle;

        _uiWindow.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();

        // Dalamud 只按首个空格切分命令，所以 "/rb mqs" 这类注册实际不会命中，
        // 子命令总是作为 arguments 传进来。若将来匹配规则变为最长前缀，
        // 这些 handler 会收到空 arguments，这里做兼容以免所有子命令突然失效。
        if (args.Length == 0 && command.StartsWith("/rb ", System.StringComparison.OrdinalIgnoreCase))
            args = command.Substring(4).Trim();

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
        else if (args.StartsWith("切号", System.StringComparison.Ordinal))
            _uiWindow.SwitchCharacter(args.Substring(2).Trim());
        else
            _uiWindow.Toggle();
    }

    private void Draw() => _uiWindow.Draw();

    private void Toggle() => _uiWindow.Toggle();
}
