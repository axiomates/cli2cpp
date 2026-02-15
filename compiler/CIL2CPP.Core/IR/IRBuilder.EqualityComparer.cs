using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// EqualityComparer&lt;T&gt; method interception.
/// EqualityComparer is a BCL generic class whose method bodies reference deep BCL internals
/// (ComparerHelpers, DelegateEqualityComparer, etc.) that aren't available in single-assembly mode.
/// We intercept calls and emit simple inline C++ implementations.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Check if a type reference is System.Collections.Generic.EqualityComparer`1 (any instantiation).
    /// </summary>
    private static bool IsEqualityComparerType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.Collections.Generic.EqualityComparer`1";
    }

    /// <summary>
    /// Check if an open type name is EqualityComparer`1 (used in CreateGenericSpecializations).
    /// </summary>
    internal static bool IsEqualityComparerBclGenericType(string openTypeName)
    {
        return openTypeName == "System.Collections.Generic.EqualityComparer`1";
    }

    /// <summary>
    /// Resolve the element type argument for an EqualityComparer&lt;T&gt; instantiation.
    /// </summary>
    private static string? GetEqualityComparerTypeArg(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType git && git.GenericArguments.Count > 0)
            return git.GenericArguments[0].FullName;
        return null;
    }

    /// <summary>
    /// Handle calls to EqualityComparer&lt;T&gt; methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitEqualityComparerCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsEqualityComparerType(methodRef.DeclaringType)) return false;

        var typeArg = GetEqualityComparerTypeArg(methodRef.DeclaringType);
        var typeCpp = typeArg != null ? CppNameMapper.GetCppTypeForDecl(typeArg) : "cil2cpp::Object*";
        var declTypeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
        bool isValueType = typeArg != null && CppNameMapper.IsValueType(typeArg);

        switch (methodRef.Name)
        {
            case "get_Default":
            {
                // Return a singleton: allocate on first call, cache in statics
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if (!{declTypeCpp}_statics.f__Default_k__BackingField) {{ " +
                           $"{declTypeCpp}_statics.f__Default_k__BackingField = " +
                           $"({declTypeCpp}*)cil2cpp::gc::alloc(sizeof({declTypeCpp}), &{declTypeCpp}_TypeInfo); }}"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {declTypeCpp}_statics.f__Default_k__BackingField;"
                });
                stack.Push(tmp);
                return true;
            }
            case "Equals" when methodRef.Parameters.Count == 2:
            {
                // Equals(T x, T y) → compare values
                var y = stack.Count > 0 ? stack.Pop() : "0";
                var x = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr"; // pop 'this'
                var tmp = $"__t{tempCounter++}";
                if (isValueType)
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = ({x} == {y});"
                    });
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = cil2cpp::object_equals((cil2cpp::Object*){x}, (cil2cpp::Object*){y});"
                    });
                }
                stack.Push(tmp);
                return true;
            }
            case "GetHashCode" when methodRef.Parameters.Count == 1:
            {
                // GetHashCode(T obj) → hash value
                var obj = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr"; // pop 'this'
                var tmp = $"__t{tempCounter++}";
                if (isValueType)
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = static_cast<int32_t>({obj});"
                    });
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = cil2cpp::object_get_hash_code((cil2cpp::Object*){obj});"
                    });
                }
                stack.Push(tmp);
                return true;
            }
            case ".ctor":
            {
                // Constructor — just pop 'this', no-op (base Object ctor is enough)
                if (stack.Count > 0) stack.Pop();
                return true;
            }
            case "System.Collections.IEqualityComparer.GetHashCode":
            {
                // IEqualityComparer.GetHashCode(object obj)
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::object_get_hash_code((cil2cpp::Object*){obj});"
                });
                stack.Push(tmp);
                return true;
            }
            case "System.Collections.IEqualityComparer.Equals":
            {
                // IEqualityComparer.Equals(object x, object y)
                var y = stack.Count > 0 ? stack.Pop() : "nullptr";
                var x = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::object_equals((cil2cpp::Object*){x}, (cil2cpp::Object*){y});"
                });
                stack.Push(tmp);
                return true;
            }
            case "IndexOf" or "LastIndexOf":
            {
                // IndexOf/LastIndexOf(T[] array, T value, int startIndex, int count)
                // Simple linear search implementation
                var count = stack.Count > 0 ? stack.Pop() : "0";
                var startIndex = stack.Count > 0 ? stack.Pop() : "0";
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var array = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr"; // pop 'this'
                var tmp = $"__t{tempCounter++}";
                if (methodRef.Name == "IndexOf")
                {
                    if (isValueType)
                    {
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"int32_t {tmp} = -1; " +
                                   $"for (int32_t __i = {startIndex}; __i < {startIndex} + {count}; __i++) " +
                                   $"{{ if (cil2cpp::array_get<{typeCpp}>({array}, __i) == {value}) {{ {tmp} = __i; break; }} }}"
                        });
                    }
                    else
                    {
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"int32_t {tmp} = -1; " +
                                   $"for (int32_t __i = {startIndex}; __i < {startIndex} + {count}; __i++) " +
                                   $"{{ if (cil2cpp::object_equals((cil2cpp::Object*)cil2cpp::array_get<{typeCpp}>({array}, __i), (cil2cpp::Object*){value})) {{ {tmp} = __i; break; }} }}"
                        });
                    }
                }
                else // LastIndexOf
                {
                    if (isValueType)
                    {
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"int32_t {tmp} = -1; " +
                                   $"for (int32_t __i = {startIndex}; __i >= {startIndex} - {count} + 1; __i--) " +
                                   $"{{ if (cil2cpp::array_get<{typeCpp}>({array}, __i) == {value}) {{ {tmp} = __i; break; }} }}"
                        });
                    }
                    else
                    {
                        block.Instructions.Add(new IRRawCpp
                        {
                            Code = $"int32_t {tmp} = -1; " +
                                   $"for (int32_t __i = {startIndex}; __i >= {startIndex} - {count} + 1; __i--) " +
                                   $"{{ if (cil2cpp::object_equals((cil2cpp::Object*)cil2cpp::array_get<{typeCpp}>({array}, __i), (cil2cpp::Object*){value})) {{ {tmp} = __i; break; }} }}"
                        });
                    }
                }
                stack.Push(tmp);
                return true;
            }
            default:
            {
                // For any other method (Create, etc.), consume all args + this, push nullptr
                for (int i = 0; i < methodRef.Parameters.Count; i++)
                    if (stack.Count > 0) stack.Pop();
                if (methodRef.HasThis && stack.Count > 0)
                    stack.Pop();
                if (!IsVoidReturnType(methodRef.ReturnType))
                {
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = nullptr; // EqualityComparer stub: {methodRef.Name}"
                    });
                    stack.Push(tmp);
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Handle newobj for EqualityComparer&lt;T&gt;.
    /// Returns true if the constructor was handled.
    /// </summary>
    private bool TryEmitEqualityComparerNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsEqualityComparerType(methodRef.DeclaringType)) return false;

        // Pop constructor args (if any)
        for (int i = 0; i < methodRef.Parameters.Count; i++)
            if (stack.Count > 0) stack.Pop();

        var declTypeCpp = GetMangledTypeNameForRef(methodRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = ({declTypeCpp}*)cil2cpp::gc::alloc(sizeof({declTypeCpp}), &{declTypeCpp}_TypeInfo);"
        });
        stack.Push(tmp);
        return true;
    }
}
