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
        foreach (var pkg in plan.Packages)
        {
            if (pkg.Manager is ManagerKind.Apt or ManagerKind.Snap or ManagerKind.Deb) return true;
            if (pkg.Spec.UserScope == false) return true;
        }
        return false;
    }
}
