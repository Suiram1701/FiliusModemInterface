using System.Diagnostics;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Vermittelungsschicht;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.vermittlungsschicht.IpPaket")]
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
    
    [JavaField("data")] public object Data { get; set; }

    public override string ToString() => $"[id={Id}, ttl={TTL}, protocol={Protocol}, src={SourceIP}, dest={DestinationIP}]";
}