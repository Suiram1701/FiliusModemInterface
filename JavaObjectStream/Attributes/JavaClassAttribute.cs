namespace FiliusModemInterface.JavaObjectStream.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class JavaClassAttribute(string className) : Attribute
{
    public string ClassName { get; } = className;
}