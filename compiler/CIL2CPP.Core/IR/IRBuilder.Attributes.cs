using Mono.Cecil;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    /// <summary>
    /// Collect custom attributes from a Cecil ICustomAttributeProvider.
    /// Only extracts primitive and string constructor arguments.
    /// </summary>
    private static List<IRCustomAttribute> CollectAttributes(ICustomAttributeProvider provider)
    {
        var result = new List<IRCustomAttribute>();
        if (!provider.HasCustomAttributes) return result;

        foreach (var attr in provider.CustomAttributes)
        {
            // Skip compiler-generated attributes that aren't useful for reflection
            var attrTypeName = attr.AttributeType.FullName;
            if (IsCompilerInternalAttribute(attrTypeName)) continue;

            var irAttr = new IRCustomAttribute
            {
                AttributeTypeName = attrTypeName,
                AttributeTypeCppName = CppNameMapper.MangleTypeName(attrTypeName),
            };

            // Collect primitive/string constructor arguments
            if (attr.HasConstructorArguments)
            {
                foreach (var arg in attr.ConstructorArguments)
                {
                    if (IsSupportedAttributeArgType(arg.Type))
                    {
                        irAttr.ConstructorArgs.Add(new IRAttributeArg
                        {
                            TypeName = arg.Type.FullName,
                            Value = arg.Value,
                        });
                    }
                }
            }

            result.Add(irAttr);
        }

        return result;
    }

    /// <summary>
    /// Check if an attribute type name is a compiler-internal attribute
    /// that should not be exposed through reflection.
    /// </summary>
    private static bool IsCompilerInternalAttribute(string attrTypeName) => attrTypeName switch
    {
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute" => true,
        "System.Runtime.CompilerServices.NullableAttribute" => true,
        "System.Runtime.CompilerServices.NullableContextAttribute" => true,
        "System.Runtime.CompilerServices.IsReadOnlyAttribute" => true,
        "System.Runtime.CompilerServices.IsByRefLikeAttribute" => true,
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute" => true,
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute" => true,
        "System.Diagnostics.CodeAnalysis.ScopedRefAttribute" => true,
        "System.ParamArrayAttribute" => true,
        "Microsoft.CodeAnalysis.EmbeddedAttribute" => true,
        _ => false,
    };

    /// <summary>
    /// Check if a type is supported for attribute argument storage.
    /// </summary>
    private static bool IsSupportedAttributeArgType(TypeReference type)
    {
        return type.FullName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" or
            "System.Int16" or "System.UInt16" or
            "System.Int32" or "System.UInt32" or
            "System.Int64" or "System.UInt64" or
            "System.Single" or "System.Double" or
            "System.Char" or "System.String" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Populate custom attributes on all IR types, methods, and fields.
    /// Called as a separate pass after type shells and method shells are created.
    /// </summary>
    private void PopulateCustomAttributes()
    {
        foreach (var typeDef in _allTypes!)
        {
            if (typeDef.HasGenericParameters) continue;

            var cecilType = typeDef.GetCecilType();
            if (cecilType == null) continue;

            if (!_typeCache.TryGetValue(typeDef.FullName, out var irType)) continue;

            // Type attributes
            irType.CustomAttributes.AddRange(CollectAttributes(cecilType));

            // Field attributes
            foreach (var field in cecilType.Fields)
            {
                var irField = irType.Fields.FirstOrDefault(f => f.Name == field.Name)
                    ?? irType.StaticFields.FirstOrDefault(f => f.Name == field.Name);
                if (irField != null)
                {
                    irField.CustomAttributes.AddRange(CollectAttributes(field));
                }
            }

            // Method attributes
            foreach (var method in cecilType.Methods)
            {
                var irMethod = irType.Methods.FirstOrDefault(m =>
                    m.Name == method.Name && m.Parameters.Count == method.Parameters.Count);
                if (irMethod != null)
                {
                    irMethod.CustomAttributes.AddRange(CollectAttributes(method));
                }
            }
        }
    }
}
