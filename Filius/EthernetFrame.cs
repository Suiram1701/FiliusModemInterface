using System.Diagnostics;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.netzzugangsschicht.EthernetFrame")]
public class EthernetFrame : ProtocolDataUnit
{
    public const string IP = "0x800";

    public const string ARP = "0x806";
    
    [JavaField("zielMacAdresse")] public string SourceMac  { get; set; }

    [JavaField("quellMacAdresse")] public string DestinationMac { get; set; }
    
    [JavaField("typ")] public string Type { get; set; }
    
    [JavaField("daten")] public ProtocolDataUnit Payload { get; set; }
    
    [JavaField("readByLauscherForMac")] public Filius.Utils.HashSet ReadByLauscherForMac { get; set; }     // I have no clue what this does

    public override string ToString() => $"[src={SourceMac}, dest={DestinationMac}, type={Type}, payload={Payload}]";
}