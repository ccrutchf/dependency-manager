namespace DependencyManager.Util;

public sealed record RebootSignal(string Source, string Reason);

/// <summary>
/// Detects whether a previously-applied update means a reboot is required.
/// The criterion is uniform across OSes: a component loaded at boot (kernel,
/// initrd, libc, init system, firmware) was replaced. Each OS publishes a
/// canonical signal for this; the per-OS helpers below are pure functions over
/// that signal so they can be unit-tested with file/symlink probes, while
/// <see cref="Check"/> wires the real filesystem in for the running platform.
///
/// macOS is intentionally omitted: softwareupdate's own <c>--restart</c> flag
/// handles its restart-required updates inline, and depend's verification step
/// already surfaces any that were skipped.
/// </summary>
public static class RebootCheck
{
    /// <summary>
    /// Debian/Ubuntu: <c>update-notifier-common</c>'s postinst hooks touch
    /// <c>/var/run/reboot-required</c> when kernel, libc, systemd, or dbus
    /// updates land. The companion <c>.pkgs</c> file lists which packages
    /// triggered it.
    /// </summary>
    public static RebootSignal? Debian(
        Func<string, bool> fileExists,
        Func<string, string?> readText)
    {
        const string flag = "/var/run/reboot-required";
        const string pkgs = "/var/run/reboot-required.pkgs";

        if (!fileExists(flag)) return null;

        var raw = readText(pkgs);
        var names = string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var reason = names.Length == 0
            ? "/var/run/reboot-required present"
            : $"packages: {string.Join(", ", names)}";

        return new RebootSignal("apt/dpkg", reason);
    }

    /// <summary>
    /// NixOS: <c>/run/booted-system</c> reflects the system that's currently
    /// running; <c>/run/current-system</c> reflects the most recently activated
    /// generation. When the resolved kernel symlinks differ, a reboot is needed
    /// to pick up the new kernel.
    /// </summary>
    public static RebootSignal? Nixos(Func<string, string?> resolveSymlink)
    {
        var booted = resolveSymlink("/run/booted-system/kernel");
        var current = resolveSymlink("/run/current-system/kernel");

        if (booted is null || current is null) return null;
        if (string.Equals(booted, current, StringComparison.Ordinal)) return null;

        return new RebootSignal("nixos", "booted kernel differs from current generation");
    }

    public static IReadOnlyList<RebootSignal> Check()
    {
        var signals = new List<RebootSignal>();
        if (!OperatingSystem.IsLinux()) return signals;

        var debian = Debian(File.Exists, ReadTextSafely);
        if (debian is not null) signals.Add(debian);

        var nixos = Nixos(ResolveLinkSafely);
        if (nixos is not null) signals.Add(nixos);

        return signals;
    }

    private static string? ReadTextSafely(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    private static string? ResolveLinkSafely(string path)
    {
        try { return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName; }
        catch { return null; }
    }
}
