using System.Text.Json;
using System.Text.Json.Serialization;

namespace BandwidthTester.Core;

/// <summary>Loads/saves <see cref="AppConfig"/> (an unbounded list of socket profiles) as JSON.</summary>
public static class ConfigStore
{
    private static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<AppConfig>(stream, CreateOptions())
                     ?? throw new FormatException($"Config file '{path}' did not contain a valid configuration.");

        foreach (var socket in config.Sockets)
            socket.Validate();

        return config;
    }

    public static AppConfig LoadFromString(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, CreateOptions())
                     ?? throw new FormatException("Config JSON did not contain a valid configuration.");
        foreach (var socket in config.Sockets)
            socket.Validate();
        return config;
    }

    public static void Save(AppConfig config, string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, config, CreateOptions());
    }

    public static string SaveToString(AppConfig config) =>
        JsonSerializer.Serialize(config, CreateOptions());
}
