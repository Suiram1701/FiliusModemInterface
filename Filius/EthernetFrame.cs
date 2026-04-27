using System.Diagnostics;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.netzzugangsschicht.EthernetFrame", SerialVersionUid = 1L)]
public class EthernetFrame : ProtocolDataUnit
{
    public const string IP = "0x800";

    public const string ARP = "0x806";
    
    [JavaField("zielMacAdresse")] public string DestinationMac { get; set; }
    
    [JavaField("quellMacAdresse")] public string SourceMac  { get; set; }
    
    [JavaField("typ")] public string Type { get; set; }
    
    [JavaField("daten")] public ProtocolDataUnit Payload { get; set; }

    [JavaField("readByLauscherForMac", ClassName = "java/util/Set")]
    public Filius.Utils.HashSet ReadByLauscherForMac { get; set; } = [];

    public override string ToString() => $"[src={SourceMac}, dest={DestinationMac}, type={Type}, payload={Payload}]";
}