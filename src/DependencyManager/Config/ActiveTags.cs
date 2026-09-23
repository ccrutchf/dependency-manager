using DependencyManager.Util;

namespace DependencyManager.Config;

public enum TagSource
{
    None,
    Cli,
    Env,
}

/// <summary>
/// The machine tags in effect for this run, used by blocks' <c>tags</c>/<c>exclude_tags</c>
/// filters. Kept separate from <see cref="Util.PlatformInfo"/>, which describes the OS.
/// </summary>
public sealed record ActiveTags(IReadOnlySet<string> Names, TagSource Source)
{
    public const string EnvVar = "DEPEND_TAGS";

    public static ActiveTags None { get; } =
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), TagSource.None);

    public static ActiveTags FromEnvironment(IReadOnlyList<string>? cli) =>
        Resolve(cli, Environment.GetEnvironmentVariable(EnvVar));

    /// <summary>
    /// Any <c>--tag</c> value replaces the env var entirely (even a blank one, so
    /// <c>--tag ''</c> ignores <c>DEPEND_TAGS</c> for a run). Values are comma-split,
    /// trimmed, and compared case-insensitively.
    /// </summary>
    public static ActiveTags Resolve(IReadOnlyList<string>? cli, string? env)
    {
        if (cli is { Count: > 0 })
            return new ActiveTags(Parse(cli), TagSource.Cli);

        var fromEnv = Parse([env ?? string.Empty]);
        return fromEnv.Count > 0 ? new ActiveTags(fromEnv, TagSource.Env) : None;
    }

    public bool Contains(string tag) => Names.Contains(tag);

    /// <summary>
    /// Active tags that no block in <paramref name="config"/> mentions in <c>tags</c> or
    /// <c>exclude_tags</c> — almost certainly typos. Every block counts, whatever its platform.
    /// </summary>
    public IReadOnlyList<string> UnknownIn(ConfigFile config)
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in config.Blocks.Values)
        {
            declared.UnionWith(block.Tags ?? []);
            declared.UnionWith(block.ExcludeTags ?? []);
        }
        return Names.Where(t => !declared.Contains(t)).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string Describe() => Source switch
    {
        TagSource.Cli => $"{Format()} (from --tag)",
        TagSource.Env => $"{Format()} (from {EnvVar})",
        _ => "none",
    };

    private string Format() =>
        Names.Count == 0 ? "none" : string.Join(", ", Names.Order(StringComparer.OrdinalIgnoreCase));

    private static HashSet<string> Parse(IEnumerable<string> values) =>
        new(values
                .SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.OrdinalIgnoreCase);
}

public sealed record TagGuardResult(IReadOnlyList<string> Unknown, bool Refuse, string? Message);

/// <summary>
/// A mistyped or missing tag silently shrinks the plan, and a prune would then remove real
/// packages. Destructive runs (<c>install --prune</c>, <c>prune --apply</c>) refuse when an
/// active tag is unknown, or when no tags were given at all but this platform has
/// <c>tags:</c>-gated blocks (e.g. <c>DEPEND_TAGS</c> not exported under cron). An explicit
/// <c>--tag ''</c> is a deliberate untagged run and passes. Non-destructive runs only warn
/// on unknown tags.
/// </summary>
public static class TagGuard
{
    public static TagGuardResult Check(ConfigFile config, PlatformInfo platform, ActiveTags tags, bool destructive)
    {
        var unknown = tags.UnknownIn(config);
        if (unknown.Count > 0) return UnknownTags(unknown, tags, destructive);

        var ok = new TagGuardResult(unknown, Refuse: false, Message: null);
        if (!destructive || tags.Source != TagSource.None) return ok;

        var gated = config.Blocks
            .Where(b => b.Value.Tags is { Count: > 0 } && BlockFilter.MatchesPlatform(b.Value, platform))
            .Select(b => b.Key)
            .ToList();
        if (gated.Count == 0) return ok;

        return new TagGuardResult(unknown, Refuse: true,
            $"error: no tags are active (no --tag, {ActiveTags.EnvVar} unset), but tag-gated block(s) " +
            $"{string.Join(", ", gated)} apply to this platform; refusing to prune them away. " +
            "Pass --tag <name>, or --tag '' to prune as an untagged machine on purpose.");
    }

    private static TagGuardResult UnknownTags(IReadOnlyList<string> unknown, ActiveTags tags, bool destructive)
    {
        var origin = tags.Source == TagSource.Env ? ActiveTags.EnvVar : "--tag";
        var list = string.Join(", ", unknown.Select(t => $"'{t}'"));
        var message = destructive
            ? $"error: active tag(s) {list} (from {origin}) are not used by any block's tags/exclude_tags; " +
              "refusing to prune against a plan that may be missing packages. Fix the tag or the config."
            : $"warning: active tag(s) {list} (from {origin}) are not used by any block's tags/exclude_tags";
        return new TagGuardResult(unknown, destructive, message);
    }
}
