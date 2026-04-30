using System.Diagnostics;
using FiliusModemInterface.Filius.Transportschicht;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Vermittlungsschicht;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.vermittlungsschicht.IpPaket", SerialVersionUid = -152582862425589970)]     // Version Uid took from running filius instance
public class IpPaket : ProtocolDataUnit
{
    public const int TCP = 6;
    public const int UDP = 17;
    
    [JavaField("identification")] public long Id { get; set; }
    
    [JavaField("identificationCounter")] public long IdCounter { get; set; }
    
    [JavaField("sender")] public string SourceIP { get; set; }
    
    [JavaField("empfaenger")] public string DestinationIP { get; set; }
    
    [JavaField("ttl")] public int TTL { get; set; }
    
    [JavaField("protocol")] public int Protocol { get; set; }
    
    [JavaField("data", ClassName = "filius.software.transportschicht.Segment")]
    public Segment Data { get; set; }

    public override string ToString() => $"[id={Id}, ttl={TTL}, protocol={Protocol}, src={SourceIP}, dest={DestinationIP}]";
}