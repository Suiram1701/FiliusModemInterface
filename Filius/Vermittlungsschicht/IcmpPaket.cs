using System.Diagnostics;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Vermittlungsschicht;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.vermittlungsschicht.IcmpPaket", SerialVersionUid = 9166540775581057810)]     // Version Uid took from running filius instance
public class IcmpPaket : IpPaket
{
    public const int Icmp_Protocol = 1;
    
    [JavaField("identifier")] public int Identifier { get; set; }
    
    [JavaField("seqNr")] public int SeqNr { get; set; }

    [JavaField("icmpType")] public int Type { get; set; }
    
    [JavaField("icmpCode")] public int Code { get; set; }
    
    [JavaField("payload", ClassName = "filius.software.vermittlungsschicht.IpPaket")]
    public IpPaket Payload { get; set; }

    public override string ToString() => $"[id={Id}, ttl={TTL}, protocol={Protocol}, src={SourceIP}, dest={DestinationIP}, icmpType={Type}, icmpCode={Code}, identifier={Identifier}, seqNr={SeqNr}, payload={Payload}]";

    public enum IcmpType
    {
        EchoReply = 0,
        See = 3,
        EchoRequest = 8,
        TimeExeeded = 11
    }

    public enum IcmpCode
    {
        NetworkUnreachable = 0,
        HostUnreachable = 1
    }
}