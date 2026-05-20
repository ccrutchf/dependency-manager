using DependencyManager.Config;
using DependencyManager.Managers;

namespace DependencyManager.Commands;

public static class ListCommand
{
    public static int Run()
    {
        var managers = new IPackageManager[]
        {
            new AptManager(),
            new SnapManager(),
            new FlatpakManager(),
            new DebManager(),
            new PipManager(),
            new PipxManager(),
            new ScriptManager(),
            new VsCodeManager(),
            new CargoManager(),
            new NvmManager(),
            new BrowserExtensionManager(ManagerKind.Firefox),
            new BrowserExtensionManager(ManagerKind.Zen),
            new BrowserExtensionManager(ManagerKind.Chrome),
            new BrowserExtensionManager(ManagerKind.Chromium),
            new BrowserExtensionManager(ManagerKind.Brave),
        };

        foreach (var m in managers)
        {
            var status = m.IsAvailable() ? "available" : "not available";
            Console.WriteLine($"  {m.Kind,-8}  [{status}]");
        }
        return 0;
    }
}
