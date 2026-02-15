using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Span&lt;T&gt; and ReadOnlySpan&lt;T&gt; method interception.
/// These are BCL ref structs whose method bodies are not in user assemblies.
/// We intercept calls and emit inline C++ instead.
///
/// Span&lt;T&gt; internal representation (2 fields):
///   void* _pointer  — pointer to first element (intptr_t in IL)
///   int _length     — number of elements
///
/// ReadOnlySpan&lt;T&gt; uses the same layout.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Check if a type reference is System.Span`1 (any instantiation).
    /// </summary>
    private static bool IsSpanType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.Span`1";
    }

    /// <summary>
    /// Check if a type reference is System.ReadOnlySpan`1 (any instantiation).
    /// </summary>
    private static bool IsReadOnlySpanType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.ReadOnlySpan`1";
    }

    /// <summary>
    /// Check if an open type name is a Span or ReadOnlySpan BCL generic type.
    /// Used by CreateGenericSpecializations to allow null CecilOpenType.
    /// </summary>
    internal static bool IsSpanBclGenericType(string openTypeName)
    {
        return openTypeName.StartsWith("System.Span`")
            || openTypeName.StartsWith("System.ReadOnlySpan`");
    }

    /// <summary>
    /// Create synthetic fields for Span&lt;T&gt; / ReadOnlySpan&lt;T&gt; since Cecil cannot
    /// resolve their type definitions from user assemblies.
    /// </summary>
    internal List<IRField> CreateSpanSyntheticFields(
        string openTypeName, IRType irType, Dictionary<string, string> typeParamMap)
    {
        var fields = new List<IRField>();
        // Both Span<T> and ReadOnlySpan<T> have the same two fields:
        // ByReference<T> _reference (effectively a pointer) + int _length
        // We represent _reference as intptr_t (a raw pointer)
        fields.Add(MakeSyntheticField("_reference", "System.IntPtr", irType));
        fields.Add(MakeSyntheticField("_length", "System.Int32", irType));
        return fields;
    }

    /// <summary>
    /// Get the C++ element type for a Span&lt;T&gt; or ReadOnlySpan&lt;T&gt;.
    /// Extracts T from GenericInstanceType.
    /// </summary>
    private static string GetSpanElementCppType(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType git && git.GenericArguments.Count > 0)
            return CppNameMapper.GetCppTypeName(git.GenericArguments[0].FullName);
        return "void";
    }

    /// <summary>
    /// Handle calls to Span&lt;T&gt; methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitSpanCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        bool isSpan = IsSpanType(methodRef.DeclaringType);
        bool isRoSpan = IsReadOnlySpanType(methodRef.DeclaringType);
        if (!isSpan && !isRoSpan) return false;

        var elemCppType = GetSpanElementCppType(methodRef.DeclaringType);

        // Wrap thisArg in parentheses to handle ldloca pattern
        string This()
        {
            var raw = stack.Count > 0 ? stack.Pop() : "nullptr";
            return raw.StartsWith("&") ? $"({raw})" : raw;
        }

        switch (methodRef.Name)
        {
            case ".ctor":
                return EmitSpanCtor(block, stack, methodRef, elemCppType, This, ref tempCounter);

            case "get_Length":
            {
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f_length;"
                });
                stack.Push(tmp);
                return true;
            }

            case "get_IsEmpty":
            {
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({thisArg}->f_length == 0);"
                });
                stack.Push(tmp);
                return true;
            }

            case "get_Item":
            {
                // get_Item(int index) — bounds-checked element access
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if (static_cast<uint32_t>({index}) >= static_cast<uint32_t>({thisArg}->f_length)) cil2cpp::throw_index_out_of_range();"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = &(static_cast<{elemCppType}*>((void*){thisArg}->f_reference))[{index}];"
                });
                stack.Push(tmp);
                return true;
            }

            case "Slice":
                return EmitSpanSlice(block, stack, methodRef, elemCppType, This, ref tempCounter);

            case "ToArray":
            {
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                // Allocate array, memcpy data
                var elemIlType = methodRef.DeclaringType is GenericInstanceType git
                    ? git.GenericArguments[0].FullName : "System.Object";
                var elemMangled = CppNameMapper.MangleTypeName(elemIlType);
                if (CppNameMapper.IsPrimitive(elemIlType))
                    _module.RegisterPrimitiveTypeInfo(elemIlType);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::array_create(&{elemMangled}_TypeInfo, {thisArg}->f_length);"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({thisArg}->f_length > 0) std::memcpy(cil2cpp::array_data({tmp}), (void*){thisArg}->f_reference, {thisArg}->f_length * sizeof({elemCppType}));"
                });
                stack.Push(tmp);
                return true;
            }

            case "GetPinnableReference":
            {
                // Returns a ref to the first element (or ref to null for empty)
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = static_cast<{elemCppType}*>((void*){thisArg}->f_reference);"
                });
                stack.Push(tmp);
                return true;
            }

            case "CopyTo":
            {
                // CopyTo(Span<T> destination) — copy elements
                var dest = This(); // destination is the argument
                var thisArg = This(); // but wait, this is instance method: stack is [this, dest]
                // Actually: instance method, stack order is [dest_pushed_last, this_below]
                // Correction: CopyTo is instance method → stack top is param, below is this
                // So: dest was popped first, then this
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({thisArg}->f_length > {dest}->f_length) cil2cpp::throw_argument();"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({thisArg}->f_length > 0) std::memcpy((void*){dest}->f_reference, (void*){thisArg}->f_reference, {thisArg}->f_length * sizeof({elemCppType}));"
                });
                return true;
            }

            case "Clear":
            case "Fill":
            {
                if (methodRef.Name == "Clear")
                {
                    var thisArg = This();
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"if ({thisArg}->f_length > 0) std::memset((void*){thisArg}->f_reference, 0, {thisArg}->f_length * sizeof({elemCppType}));"
                    });
                }
                else // Fill
                {
                    var value = stack.Count > 0 ? stack.Pop() : "0";
                    var thisArg = This();
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"for (int32_t __i = 0; __i < {thisArg}->f_length; __i++) static_cast<{elemCppType}*>((void*){thisArg}->f_reference)[__i] = {value};"
                    });
                }
                return true;
            }

            default:
                return false;
        }
    }

    private bool EmitSpanCtor(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, string elemCppType, Func<string> This, ref int tempCounter)
    {
        switch (methodRef.Parameters.Count)
        {
            case 1:
            {
                // .ctor(T[] array) — create span from array
                var array = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = This();
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f_reference = (intptr_t)({array} ? cil2cpp::array_data({array}) : nullptr); {thisArg}->f_length = {array} ? {array}->length : 0;"
                });
                return true;
            }
            case 2 when methodRef.Parameters[0].ParameterType.FullName == "System.Void*"
                      || methodRef.Parameters[0].ParameterType is PointerType:
            {
                // .ctor(void* pointer, int length)
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var pointer = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = This();
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f_reference = (intptr_t){pointer}; {thisArg}->f_length = {length};"
                });
                return true;
            }
            case 3:
            {
                // .ctor(T[] array, int start, int length)
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var start = stack.Count > 0 ? stack.Pop() : "0";
                var array = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = This();
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f_reference = (intptr_t)(static_cast<{elemCppType}*>(cil2cpp::array_data({array})) + {start}); {thisArg}->f_length = {length};"
                });
                return true;
            }
            default:
                return false;
        }
    }

    private static bool EmitSpanSlice(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, string elemCppType, Func<string> This, ref int tempCounter)
    {
        var typeCpp = CppNameMapper.GetCppTypeName(
            methodRef.DeclaringType is GenericInstanceType git2
                ? git2.FullName : methodRef.DeclaringType.FullName);

        var tmp = $"__t{tempCounter++}";

        if (methodRef.Parameters.Count == 1)
        {
            // Slice(int start)
            var start = stack.Count > 0 ? stack.Pop() : "0";
            var thisArg = This();
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{typeCpp} {tmp}; {tmp}.f_reference = (intptr_t)(static_cast<{elemCppType}*>((void*){thisArg}->f_reference) + {start}); {tmp}.f_length = {thisArg}->f_length - {start};"
            });
        }
        else
        {
            // Slice(int start, int length)
            var length = stack.Count > 0 ? stack.Pop() : "0";
            var start = stack.Count > 0 ? stack.Pop() : "0";
            var thisArg = This();
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{typeCpp} {tmp}; {tmp}.f_reference = (intptr_t)(static_cast<{elemCppType}*>((void*){thisArg}->f_reference) + {start}); {tmp}.f_length = {length};"
            });
        }
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Handle Span&lt;T&gt; constructor via newobj (rare — usually ldloca+call).
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitSpanNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        bool isSpan = IsSpanType(ctorRef.DeclaringType);
        bool isRoSpan = IsReadOnlySpanType(ctorRef.DeclaringType);
        if (!isSpan && !isRoSpan) return false;

        var typeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var elemCppType = GetSpanElementCppType(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        switch (ctorRef.Parameters.Count)
        {
            case 0:
                // Default ctor — empty span
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{typeCpp} {tmp} = {{}};"
                });
                break;
            case 1:
            {
                // .ctor(T[] array)
                var array = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{typeCpp} {tmp}; {tmp}.f_reference = (intptr_t)({array} ? cil2cpp::array_data({array}) : nullptr); {tmp}.f_length = {array} ? {array}->length : 0;"
                });
                break;
            }
            case 2:
            {
                // .ctor(void* pointer, int length)
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var pointer = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{typeCpp} {tmp}; {tmp}.f_reference = (intptr_t){pointer}; {tmp}.f_length = {length};"
                });
                break;
            }
            case 3:
            {
                // .ctor(T[] array, int start, int length)
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var start = stack.Count > 0 ? stack.Pop() : "0";
                var array = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{typeCpp} {tmp}; {tmp}.f_reference = (intptr_t)(static_cast<{elemCppType}*>(cil2cpp::array_data({array})) + {start}); {tmp}.f_length = {length};"
                });
                break;
            }
            default:
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{typeCpp} {tmp} = {{}};"
                });
                break;
        }
        stack.Push(tmp);
        return true;
    }
}
