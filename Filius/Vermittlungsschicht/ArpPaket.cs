using System.Diagnostics;
using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Vermittlungsschicht;

[DebuggerDisplay("{ToString(),nq}")]
[JavaClass("filius.software.vermittlungsschicht.ArpPaket", SerialVersionUid = 1L)]
public class ArpPaket : ProtocolDataUnit
{
    public const int Request = 1;
    public const int Reply = 2;
    
    [JavaField("arpPacketNumberCounter")] public long ArpPacketNumberCounter { get; set; }
    
    [JavaField("protokollTyp")] public string Type { get; set; }

    [JavaField("operation")] public int Operation { get; set; } = 1;
    
    [JavaField("senderMAC")] public string SourceMac { get; set; }
    
    [JavaField("senderIP")] public string SourceIP { get; set; }
    
    [JavaField("targetMAC")] public string TargetMac { get; set; }
    
    [JavaField("targetIP")] public string TargetIP { get; set; }
    
    [JavaField("arpPacketNumber")] public long ArpPacketNumber { get; set; }

    public override string ToString() => $"[op={(Operation == Request ? "REQUEST" : "REPLY")}, source={SourceMac}|{SourceIP}, target={TargetMac}|{TargetIP}]";
}