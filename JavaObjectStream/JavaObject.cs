namespace FiliusModemInterface.JavaObjectStream;

public sealed class JavaObject(string className)
{
    public string ClassName { get; } = className;
    
    public Dictionary<string, object> Fields { get; } = new();
}