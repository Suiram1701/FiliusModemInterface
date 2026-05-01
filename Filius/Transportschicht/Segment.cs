using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Transportschicht;

[JavaClass("filius.software.transportschicht.Segment", SerialVersionUid = 1294832790258489668L)]
public class Segment : ProtocolDataUnit
{
    [JavaField("quellPort")] public int SourcePort { get; set; }
    
    [JavaField("zielPort")] public int DestinationPort { get; set; }
    
    [JavaField("pruefSumme")] public int Checksum { get; set; }
    
    [JavaField("daten")] public string Data { get; set; }
}