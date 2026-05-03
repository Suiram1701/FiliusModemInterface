using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FiliusModemInterface.Filius;
using FiliusModemInterface.Filius.Dns;
using FiliusModemInterface.Filius.Transportschicht;
using FiliusModemInterface.Filius.Vermittlungsschicht;
using static FiliusModemInterface.Program;

namespace FiliusModemInterface;

public class NatServer(IPAddress ip, int port, string natMac, IPAddress natIp) : FiliusServer(ip, port)
{
    private readonly string _natMac = natMac.ToLower();
    private readonly string _natIp = natIp.ToString();

    private readonly ConcurrentDictionary<string, string> _arpTable = [];
    
    private readonly ConcurrentDictionary<Connection, (UdpClient, Task)> _udpConnTrack = [];

    public override Task RunAsync(CancellationToken ct)
    {
        LogInfo($"Starts responding on {_natIp} with {_natMac}");
        return base.RunAsync(ct);
    }

    protected override async Task HandleFrameAsync(int sourcePort, EthernetFrame frame, CancellationToken ct)
    {
        if (frame.Payload is ArpPaket { Operation: ArpPaket.Request } arp && arp.TargetIP == _natIp)
        {
            EthernetFrame response = new()
            {
                SourceMac = _natMac,
                DestinationMac = frame.SourceMac,
                Type = EthernetFrame.ARP,
                Payload = new ArpPaket
                {
                    ArpPacketNumber = 0,
                    ArpPacketNumberCounter = 0,
                    Type = EthernetFrame.IP,
                    Operation = ArpPaket.Reply,
                    SourceMac = _natMac,
                    SourceIP = arp.TargetIP,
                    TargetMac = frame.SourceMac,
                    TargetIP = arp.SourceIP
                }
            };
            await TryWriteClientAsync(sourcePort, response, ct).ConfigureAwait(false);
            return;
        }
        
        if (frame.DestinationMac == _natMac)
            await HandleIncomingPaketAsync(sourcePort, frame, ct);
        if (frame.Payload is IpPaket paket)
            _arpTable[paket.SourceIP] = frame.SourceMac;
        
        await base.HandleFrameAsync(sourcePort, frame, ct);
    }

    private async Task HandleIncomingPaketAsync(int sourcePort, EthernetFrame frame, CancellationToken ct)
    {
        ProtocolDataUnit paket = frame.Payload;
        switch (paket)
        {
            case IcmpPaket { Type: (int)IcmpPaket.IcmpType.EchoRequest } icmp:
                await TryWriteClientAsync(sourcePort, new EthernetFrame
                {
                    SourceMac = _natMac,
                    DestinationMac = frame.SourceMac,
                    Type = EthernetFrame.IP,
                    Payload = BuildIcmpPingResponse(icmp)
                }, ct);
                break;
            case IpPaket { Protocol: IpPaket.UDP } udpPaket:     // Initialize UdpClient when not already done and send data
                Connection connection = new(udpPaket.SourceIP, udpPaket.Data.SourcePort, udpPaket.DestinationIP, udpPaket.Data.DestinationPort);
                UdpClient udpClient = _udpConnTrack.GetOrAdd(connection, _ =>
                {
                    UdpClient client = new(udpPaket.DestinationIP, udpPaket.Data.DestinationPort);
                    return (client, HandleUdpConnectionAsync(connection, client, ct));     // HandleUdpConnectionAsync awaits to receive data and sends it back to filius
                }).Item1;

                ReadOnlyMemory<byte> bytes;
                if (udpPaket.Data.DestinationPort == 53)     // DNS via Udp is hardcoded to run on :53. Cannot be sent raw because filius uses a different format
                {
                    using MemoryStream memory = new();
                    DnsMessage message = DnsMessage.Parse(udpPaket.Data.Data);
                    message.Serialize(memory);
                    
                    bytes = memory.GetBuffer().AsMemory(0, (int)memory.Length);
                }
                else
                {
                    bytes = Encoding.UTF8.GetBytes(udpPaket.Data.Data);
                }

                await udpClient.SendAsync(bytes, ct);
                break;
            default:
                LogWarning($"Didn't handled paket: {paket}");
                break;
        }
    }

    private IcmpPaket BuildIcmpPingResponse(IcmpPaket paket)
    {
        return new IcmpPaket
        {
            Id = paket.Id + 1,
            SourceIP = paket.DestinationIP,
            DestinationIP = paket.SourceIP,
            TTL = paket.TTL,
            Protocol = IcmpPaket.Icmp_Protocol,
            
            Identifier = paket.Identifier,
            SeqNr = paket.SeqNr,
            Type = (int)IcmpPaket.IcmpType.EchoReply,
            Code = paket.Code,
            Payload = null!
        };
    }

    private async Task HandleUdpConnectionAsync(Connection connection, UdpClient udpClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result = await udpClient.ReceiveAsync(ct);

            string data;
            if (connection.DestPort == 53)
            {
                using MemoryStream memory = new(result.Buffer);
                DnsMessage message = DnsMessage.Deserialize(memory);
                message.FormatForFilius();
                
                data = message.ToString();
            }
            else
            {
                data = Encoding.UTF8.GetString(result.Buffer);
            }
            
            EthernetFrame response = new()
            {
                SourceMac = _natMac,
                DestinationMac = _arpTable[connection.SourceIp],
                Type = EthernetFrame.IP,
                Payload = new IpPaket
                {
                    Id = 0,
                    SourceIP = connection.DestIp,
                    DestinationIP = connection.SourceIp,
                    TTL = 64,
                    Protocol = IpPaket.UDP,
                    Data = new UdpSegment
                    {
                        SourcePort = connection.DestPort,
                        DestinationPort = connection.SourcePort,
                        Data = data,
                        Length = data.Length
                    }
                }
            };

            int targetPort = _macTable[_arpTable[connection.SourceIp]];
            await TryWriteClientAsync(targetPort, response, ct).ConfigureAwait(false);
        }
    }
    
    private record Connection(string SourceIp, int SourcePort, string DestIp, int DestPort);
}