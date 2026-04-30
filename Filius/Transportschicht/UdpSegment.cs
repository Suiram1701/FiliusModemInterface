using FiliusModemInterface.JavaObjectStream.Attributes;

namespace FiliusModemInterface.Filius.Transportschicht;

[JavaClass("filius.software.transportschicht.UdpSegment", SerialVersionUid = 1L)]
public class UdpSegment : Segment
{
    [JavaField("laenge")] public int Length { get; set; }
}