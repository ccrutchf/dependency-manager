using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DependencyManager.Config;

public static class ConfigLoader
{
    public static ConfigFile Load(string path)
    {
        var yaml = File.ReadAllText(path);
        return Parse(yaml);
    }

    public static ConfigFile Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new BlockYamlConverter())
            .Build();

        var blocks = deserializer.Deserialize<Dictionary<string, Block>>(yaml)
            ?? new Dictionary<string, Block>();

        return new ConfigFile(blocks);
    }
}
