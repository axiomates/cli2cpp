namespace CIL2CPP.Core.IR;

/// <summary>
/// Represents a custom attribute applied to a type, method, or field.
/// </summary>
public class IRCustomAttribute
{
    /// <summary>Full IL name of the attribute type (e.g., "System.ObsoleteAttribute")</summary>
    public string AttributeTypeName { get; set; } = "";

    /// <summary>C++ mangled name of the attribute type</summary>
    public string AttributeTypeCppName { get; set; } = "";

    /// <summary>Constructor arguments (primitives + strings only)</summary>
    public List<IRAttributeArg> ConstructorArgs { get; } = new();
}

/// <summary>
/// Represents a single constructor argument of a custom attribute.
/// Only primitive types and strings are supported.
/// </summary>
public class IRAttributeArg
{
    /// <summary>IL type name of the argument (e.g., "System.String")</summary>
    public string TypeName { get; set; } = "";

    /// <summary>The argument value (null, string, or boxed primitive)</summary>
    public object? Value { get; set; }
}
