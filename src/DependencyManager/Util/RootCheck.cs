using System.Runtime.InteropServices;
using DependencyManager.Config;

namespace DependencyManager.Util;

public static class RootCheck
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUid();

    public static bool IsRoot()
    {
        if (!OperatingSystem.IsLinux()) return true;
        return GetEffectiveUid() == 0;
    }

    public static bool PlanRequiresSudo(ResolvedPlan plan)
    {
        if (plan.AptPpas.Count > 0) return true;
        if (plan.AptSources.Count > 0) return true;
        foreach (var pkg in plan.Packages)
        {
            if (pkg.Manager is ManagerKind.Apt or ManagerKind.Snap or ManagerKind.Deb) return true;
            // Browser extension policies are written to /etc, <install>/distribution,
            // /var/snap, or system flatpak dirs for native/snap/system installs. Prime
            // sudo conservatively; a pure per-user flatpak setup is the only case that
            // wouldn't strictly need it.
            if (IsBrowserExtension(pkg.Manager)) return true;
            if (pkg.Spec.UserScope == false) return true;
        }
        return false;
    }

    public static bool IsBrowserExtension(ManagerKind kind) =>
        kind is ManagerKind.Firefox or ManagerKind.Zen or ManagerKind.Chrome
            or ManagerKind.Chromium or ManagerKind.Brave;
}
