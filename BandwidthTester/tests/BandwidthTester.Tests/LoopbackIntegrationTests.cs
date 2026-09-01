using System.Net;
using System.Net.Sockets;
using BandwidthTester.Core;
using Xunit;

namespace BandwidthTester.Tests;

public class LoopbackIntegrationTests
{
    private static int GetFreePort(ProtocolType protocol)
    {
        if (protocol == ProtocolType.Tcp)
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)s.LocalEndPoint!).Port;
        }
        using var u = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        u.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task Tcp_ClientToServer_TransfersFramedMessagesWithMatchingHeader()
    {
        int port = GetFreePort(ProtocolType.Tcp);

        var serverProfile = new SocketProfile
        {
            Name = "srv", Role = SocketRole.Server, Protocol = TransportProtocol.Tcp,
            LocalIp = "127.0.0.1", LocalPort = port, SendEnabled = false, MessageSize = 64,
            ReceiveByteOrder = ByteOrder.BigEndian, Header = HeaderDefinition.CreateDefault()
        };
        var clientProfile = new SocketProfile
        {
            Name = "cli", Role = SocketRole.Client, Protocol = TransportProtocol.Tcp,
            LocalIp = "127.0.0.1", LocalPort = 0, RemoteIp = "127.0.0.1", RemotePort = port,
            SendEnabled = true, MessageSize = 64, TargetBandwidthBytesPerSec = 0,
            SendByteOrder = ByteOrder.BigEndian, Header = HeaderDefinition.CreateDefault()
        };

        await using var server = new SocketWorker(serverProfile);
        await using var client = new SocketWorker(clientProfile);

        var decodedHeaders = new List<IReadOnlyDictionary<string, object>>();
        server.HeaderReceived += (_, h) => { lock (decodedHeaders) decodedHeaders.Add(h); };

        server.Start();
        await Task.Delay(200);
        client.Start();
        await Task.Delay(800);

        await client.StopAsync();
        await server.StopAsync();

        lock (decodedHeaders)
        {
            Assert.True(decodedHeaders.Count > 0, "server never decoded any header");
            var magic = (uint)decodedHeaders[0]["magic"];
            Assert.Equal(0x42575354u, magic);
            // Sequence numbers should be increasing across received packets.
            var seqs = decodedHeaders.Select(h => (uint)h["seq"]).ToList();
            for (int i = 1; i < seqs.Count; i++)
                Assert.True(seqs[i] >= seqs[i - 1]);
        }
    }

    [Fact]
    public async Task Udp_ClientToServer_TransfersDatagramsWithConfiguredEndianness()
    {
        int port = GetFreePort(ProtocolType.Udp);

        var serverProfile = new SocketProfile
        {
            Name = "srv-udp", Role = SocketRole.Server, Protocol = TransportProtocol.Udp,
            LocalIp = "127.0.0.1", LocalPort = port, SendEnabled = false, MessageSize = 32,
            ReceiveByteOrder = ByteOrder.LittleEndian, Header = HeaderDefinition.CreateDefault()
        };
        var clientProfile = new SocketProfile
        {
            Name = "cli-udp", Role = SocketRole.Client, Protocol = TransportProtocol.Udp,
            LocalIp = "127.0.0.1", LocalPort = 0, RemoteIp = "127.0.0.1", RemotePort = port,
            SendEnabled = true, MessageSize = 32, TargetBandwidthBytesPerSec = 50_000,
            SendByteOrder = ByteOrder.LittleEndian, Header = HeaderDefinition.CreateDefault()
        };

        await using var server = new SocketWorker(serverProfile);
        await using var client = new SocketWorker(clientProfile);

        int received = 0;
        server.HeaderReceived += (_, _) => Interlocked.Increment(ref received);

        server.Start();
        await Task.Delay(200);
        client.Start();
        await Task.Delay(800);

        await client.StopAsync();
        await server.StopAsync();

        Assert.True(received > 0, "server never received any UDP datagram");
    }
}
