using BandwidthTester.Core;
using Xunit;

namespace BandwidthTester.Tests;

public class ConfigStoreTests
{
    private static SocketProfile MakeProfile(string name, int port) => new()
    {
        Name = name,
        Role = SocketRole.Server,
        Protocol = TransportProtocol.Udp,
        LocalIp = "0.0.0.0",
        LocalPort = port,
        RemoteIp = "127.0.0.1",
        RemotePort = port + 1,
        SendByteOrder = ByteOrder.BigEndian,
        ReceiveByteOrder = ByteOrder.LittleEndian,
        MessageSize = 512,
        TargetBandwidthBytesPerSec = 2_000_000,
        Header = HeaderDefinition.CreateDefault()
    };

    [Fact]
    public void RoundTrip_PreservesArbitraryNumberOfProfiles()
    {
        var config = new AppConfig();
        for (int i = 0; i < 25; i++)
            config.Sockets.Add(MakeProfile($"socket-{i}", 10000 + i));

        string json = ConfigStore.SaveToString(config);
        var reloaded = ConfigStore.LoadFromString(json);

        Assert.Equal(25, reloaded.Sockets.Count);
        Assert.Equal(config.Sockets[10].LocalPort, reloaded.Sockets[10].LocalPort);
        Assert.Equal(ByteOrder.BigEndian, reloaded.Sockets[3].SendByteOrder);
        Assert.Equal(4, reloaded.Sockets[0].Header.Fields.Count);
    }

    [Fact]
    public void Load_ValidatesEachProfile_AndThrowsOnBadHeader()
    {
        var config = new AppConfig();
        var bad = MakeProfile("bad", 20000);
        bad.Header = new HeaderDefinition
        {
            Fields = { new HeaderFieldDefinition { Name = "tooShort", Type = HeaderFieldType.UInt8, Value = "1" } }
        };
        config.Sockets.Add(bad);

        string json = ConfigStore.SaveToString(config);
        Assert.Throws<FormatException>(() => ConfigStore.LoadFromString(json));
    }

    [Fact]
    public void FileRoundTrip_Works()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bwtest-config-{Guid.NewGuid():N}.json");
        try
        {
            var config = new AppConfig { Sockets = { MakeProfile("file-test", 30000) } };
            ConfigStore.Save(config, path);

            var reloaded = ConfigStore.Load(path);
            Assert.Single(reloaded.Sockets);
            Assert.Equal("file-test", reloaded.Sockets[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
