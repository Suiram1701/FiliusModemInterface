namespace FiliusModemInterface.JavaObjectStream.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class JavaFieldAttribute(string fieldName) : Attribute
{
    public string FieldName { get; } = fieldName;

    public char? TypeCode { get; init; } = null;
}