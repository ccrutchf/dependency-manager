using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace DependencyManager.Config;

public sealed class BlockYamlConverter : IYamlTypeConverter
{
    private static readonly HashSet<string> FilterKeys =
        new(StringComparer.OrdinalIgnoreCase) { "platform", "architecture", "version" };

    private static readonly HashSet<string> ProviderKeys =
        new(StringComparer.OrdinalIgnoreCase) { "apt", "snap", "flatpak", "deb", "pip", "pipx", "script", "vscode", "cargo" };

    private const string PpasKey = "ppas";

    public bool Accepts(Type type) => type == typeof(Block);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<MappingStart>();

        string platform = "all";
        string architecture = "all";
        string? version = null;
        List<string>? ppas = null;
        Dictionary<string, PackageSpec>? apt = null;
        Dictionary<string, PackageSpec>? snap = null;
        Dictionary<string, PackageSpec>? flatpak = null;
        Dictionary<string, PackageSpec>? deb = null;
        Dictionary<string, PackageSpec>? pip = null;
        Dictionary<string, PackageSpec>? pipx = null;
        Dictionary<string, PackageSpec>? script = null;
        Dictionary<string, PackageSpec>? vscode = null;
        Dictionary<string, PackageSpec>? cargo = null;

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var keyEvent = parser.Consume<Scalar>();
            var key = keyEvent.Value;

            if (FilterKeys.Contains(key))
            {
                var value = parser.Consume<Scalar>().Value;
                switch (key.ToLowerInvariant())
                {
                    case "platform": platform = value; break;
                    case "architecture": architecture = value; break;
                    case "version": version = value; break;
                }
            }
            else if (string.Equals(key, PpasKey, StringComparison.OrdinalIgnoreCase))
            {
                ppas = (List<string>?)rootDeserializer(typeof(List<string>));
            }
            else if (ProviderKeys.Contains(key))
            {
                var section = (Dictionary<string, PackageSpec>?)rootDeserializer(typeof(Dictionary<string, PackageSpec>))
                    ?? new Dictionary<string, PackageSpec>();
                switch (key.ToLowerInvariant())
                {
                    case "apt": apt = section; break;
                    case "snap": snap = section; break;
                    case "flatpak": flatpak = section; break;
                    case "deb": deb = section; break;
                    case "pip": pip = section; break;
                    case "pipx": pipx = section; break;
                    case "script": script = section; break;
                    case "vscode": vscode = section; break;
                    case "cargo": cargo = section; break;
                }
            }
            else
            {
                Console.Error.WriteLine($"warning: unknown key '{key}' in block (expected platform/architecture/version/ppas or apt/snap/flatpak/deb/pip/pipx/script/vscode/cargo)");
                _ = rootDeserializer(typeof(object));
            }
        }

        return new Block
        {
            Platform = platform,
            Architecture = architecture,
            Version = version,
            Ppas = ppas,
            Apt = apt,
            Snap = snap,
            Flatpak = flatpak,
            Deb = deb,
            Pip = pip,
            Pipx = pipx,
            Script = script,
            Vscode = vscode,
            Cargo = cargo,
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Block serialization is not supported");
}
