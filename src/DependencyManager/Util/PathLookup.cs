namespace DependencyManager.Util;

public static class PathLookup
{
    public static Func<string, bool> Probe { get; set; } = DefaultProbe;

    public static bool Exists(string name) => Probe(name);

    private static bool DefaultProbe(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        if (name.Contains('/') || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(name);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { string.Empty };

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return true;
            }
        }
        return false;
    }
}
