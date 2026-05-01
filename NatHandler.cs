using FiliusModemInterface.Filius;
using FiliusModemInterface.Filius.Vermittlungsschicht;
using static FiliusModemInterface.Program;

namespace FiliusModemInterface;

public class NatHandler
{
    public async Task<ProtocolDataUnit> HandleFrameAsync(ProtocolDataUnit paket, CancellationToken ct)
    {
        if (paket is IcmpPaket { Type: (int)IcmpPaket.IcmpType.EchoRequest } icmp)
            return BuildIcmpPingResponse(icmp);

        LogWarning($"Didn't handled paket: {paket}");
        return paket;
    }

    private IcmpPaket BuildIcmpPingResponse(IcmpPaket paket)
    {
        return new IcmpPaket()
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
}