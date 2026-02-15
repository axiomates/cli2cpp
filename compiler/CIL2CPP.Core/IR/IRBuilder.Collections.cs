using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// List&lt;T&gt; and Dictionary&lt;K,V&gt; BCL interception.
/// These are reference types whose method bodies live in mscorlib/System.Private.CoreLib
/// and cannot be compiled by single-assembly mode.
/// We intercept constructor and method calls, emitting calls to the C++ runtime's
/// type-erased list_*/dict_* functions.
/// </summary>
public partial class IRBuilder
{
    // ===== Type Detection =====

    private static bool IsListType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.Collections.Generic.List`1";
    }

    private static bool IsDictionaryType(TypeReference typeRef)
    {
        var elementName = typeRef is GenericInstanceType git
            ? git.ElementType.FullName
            : typeRef.FullName;
        return elementName == "System.Collections.Generic.Dictionary`2";
    }

    /// <summary>
    /// Check if an open type name is a collection BCL generic type.
    /// Used by CreateGenericSpecializations to detect synthetic BCL types.
    /// </summary>
    internal static bool IsCollectionBclGenericType(string openTypeName)
    {
        return openTypeName.StartsWith("System.Collections.Generic.List`")
            || openTypeName.StartsWith("System.Collections.Generic.Dictionary`");
    }

    // ===== Synthetic Fields =====

    /// <summary>
    /// Create synthetic fields for List&lt;T&gt; matching runtime ListBase layout:
    ///   Array* items, Int32 count, Int32 version, TypeInfo* elem_type
    /// </summary>
    internal List<IRField> CreateListSyntheticFields(IRType irType)
    {
        return new List<IRField>
        {
            MakeSyntheticField("_items", "System.IntPtr", irType),     // void* backing buffer
            MakeSyntheticField("_size", "System.Int32", irType),       // count
            MakeSyntheticField("_version", "System.Int32", irType),    // version
            MakeSyntheticField("_elemType", "System.IntPtr", irType),  // TypeInfo*
            MakeSyntheticField("_capacity", "System.Int32", irType),   // buffer capacity
        };
    }

    /// <summary>
    /// Create synthetic fields for Dictionary&lt;K,V&gt; matching runtime DictBase layout:
    ///   Array* buckets, IntPtr entries, Int32 count, Int32 capacity,
    ///   Int32 freeList, Int32 freeCount, IntPtr keyType, IntPtr valueType,
    ///   Int32 keySize, Int32 valueSize, Int32 entryStride
    /// </summary>
    internal List<IRField> CreateDictionarySyntheticFields(IRType irType)
    {
        return new List<IRField>
        {
            MakeSyntheticField("_buckets", "System.Array", irType),
            MakeSyntheticField("_entries", "System.IntPtr", irType),
            MakeSyntheticField("_count", "System.Int32", irType),
            MakeSyntheticField("_capacity", "System.Int32", irType),
            MakeSyntheticField("_freeList", "System.Int32", irType),
            MakeSyntheticField("_freeCount", "System.Int32", irType),
            MakeSyntheticField("_keyType", "System.IntPtr", irType),
            MakeSyntheticField("_valueType", "System.IntPtr", irType),
            MakeSyntheticField("_keySize", "System.Int32", irType),
            MakeSyntheticField("_valueSize", "System.Int32", irType),
            MakeSyntheticField("_entryStride", "System.Int32", irType),
        };
    }

    // ===== Constructor Interception =====

    private bool TryEmitListNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsListType(ctorRef.DeclaringType)) return false;

        var git = ctorRef.DeclaringType as GenericInstanceType;
        if (git == null || git.GenericArguments.Count < 1) return false;

        var elemTypeRef = git.GenericArguments[0];
        var elemTypeInfo = GetTypeInfoExpr(elemTypeRef);
        var listTypeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        // Determine initial capacity from constructor args
        string capacityArg = "0";
        if (ctorRef.Parameters.Count == 1)
        {
            // List<T>(int capacity)
            capacityArg = stack.Count > 0 ? stack.Pop() : "0";
        }

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = static_cast<{listTypeCpp}*>(cil2cpp::list_create(&{listTypeCpp}_TypeInfo, {elemTypeInfo}, {capacityArg}));"
        });
        stack.Push(tmp);
        return true;
    }

    private bool TryEmitDictionaryNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsDictionaryType(ctorRef.DeclaringType)) return false;

        var git = ctorRef.DeclaringType as GenericInstanceType;
        if (git == null || git.GenericArguments.Count < 2) return false;

        var keyTypeInfo = GetTypeInfoExpr(git.GenericArguments[0]);
        var valTypeInfo = GetTypeInfoExpr(git.GenericArguments[1]);
        var dictTypeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);
        var tmp = $"__t{tempCounter++}";

        // Consume any constructor args (capacity, comparer, etc.)
        for (int i = 0; i < ctorRef.Parameters.Count; i++)
            _ = stack.Count > 0 ? stack.Pop() : "0";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = static_cast<{dictTypeCpp}*>(cil2cpp::dict_create(&{dictTypeCpp}_TypeInfo, {keyTypeInfo}, {valTypeInfo}));"
        });
        stack.Push(tmp);
        return true;
    }

    // ===== Method Call Interception =====

    private bool TryEmitListCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsListType(methodRef.DeclaringType)) return false;

        var git = methodRef.DeclaringType as GenericInstanceType;
        if (git == null || git.GenericArguments.Count < 1) return false;

        var elemCppType = CppNameMapper.GetCppTypeName(git.GenericArguments[0].FullName);
        var name = methodRef.Name;

        switch (name)
        {
            case "Add":
            {
                var item = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ltmp = $"__lt{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{{ {elemCppType} {ltmp} = ({elemCppType}){item}; cil2cpp::list_add({listArg}, &{ltmp}); }}"
                });
                return true;
            }

            case "get_Item":
            {
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = *static_cast<{elemCppType}*>(cil2cpp::list_get_ref({listArg}, {index}));"
                });
                stack.Push(tmp);
                return true;
            }

            case "set_Item":
            {
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ltmp = $"__lt{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{{ {elemCppType} {ltmp} = ({elemCppType}){value}; cil2cpp::list_set({listArg}, {index}, &{ltmp}); }}"
                });
                return true;
            }

            case "get_Count":
            {
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::list_get_count({listArg});"
                });
                stack.Push(tmp);
                return true;
            }

            case "get_Capacity":
            {
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::list_get_capacity({listArg});"
                });
                stack.Push(tmp);
                return true;
            }

            case "RemoveAt":
            {
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::list_remove_at({listArg}, {index});"
                });
                return true;
            }

            case "Clear":
            {
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::list_clear({listArg});"
                });
                return true;
            }

            case "Contains":
            {
                var item = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ltmp = $"__lt{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{elemCppType} {ltmp} = ({elemCppType}){item};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::list_contains({listArg}, &{ltmp});"
                });
                stack.Push(tmp);
                return true;
            }

            case "IndexOf":
            {
                var item = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ltmp = $"__lt{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{elemCppType} {ltmp} = ({elemCppType}){item};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::list_index_of({listArg}, &{ltmp});"
                });
                stack.Push(tmp);
                return true;
            }

            case "Insert":
            {
                var item = stack.Count > 0 ? stack.Pop() : "0";
                var index = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ltmp = $"__lt{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{{ {elemCppType} {ltmp} = ({elemCppType}){item}; cil2cpp::list_insert({listArg}, {index}, &{ltmp}); }}"
                });
                return true;
            }

            case "Remove":
            {
                var item = stack.Count > 0 ? stack.Pop() : "0";
                var listArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ltmp = $"__lt{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{elemCppType} {ltmp} = ({elemCppType}){item};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::list_remove({listArg}, &{ltmp});"
                });
                stack.Push(tmp);
                return true;
            }

            default:
                // Unhandled List method — fall through to general handling
                return false;
        }
    }

    private bool TryEmitDictionaryCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsDictionaryType(methodRef.DeclaringType)) return false;

        var git = methodRef.DeclaringType as GenericInstanceType;
        if (git == null || git.GenericArguments.Count < 2) return false;

        var keyCppType = CppNameMapper.GetCppTypeName(git.GenericArguments[0].FullName);
        var valCppType = CppNameMapper.GetCppTypeName(git.GenericArguments[1].FullName);
        var name = methodRef.Name;

        switch (name)
        {
            case "set_Item":
            {
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var key = stack.Count > 0 ? stack.Pop() : "0";
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ktmp = $"__dk{tempCounter++}";
                var vtmp = $"__dv{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{{ {keyCppType} {ktmp} = ({keyCppType}){key}; {valCppType} {vtmp} = ({valCppType}){value}; cil2cpp::dict_set({dictArg}, &{ktmp}, &{vtmp}); }}"
                });
                return true;
            }

            case "get_Item":
            {
                var key = stack.Count > 0 ? stack.Pop() : "0";
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ktmp = $"__dk{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{keyCppType} {ktmp} = ({keyCppType}){key};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = *static_cast<{valCppType}*>(cil2cpp::dict_get_ref({dictArg}, &{ktmp}));"
                });
                stack.Push(tmp);
                return true;
            }

            case "get_Count":
            {
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::dict_get_count({dictArg});"
                });
                stack.Push(tmp);
                return true;
            }

            case "ContainsKey":
            {
                var key = stack.Count > 0 ? stack.Pop() : "0";
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ktmp = $"__dk{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{keyCppType} {ktmp} = ({keyCppType}){key};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::dict_contains_key({dictArg}, &{ktmp});"
                });
                stack.Push(tmp);
                return true;
            }

            case "Remove":
            {
                var key = stack.Count > 0 ? stack.Pop() : "0";
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ktmp = $"__dk{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{keyCppType} {ktmp} = ({keyCppType}){key};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::dict_remove({dictArg}, &{ktmp});"
                });
                stack.Push(tmp);
                return true;
            }

            case "TryGetValue":
            {
                // Stack: dict, key, &value_out
                var valueOut = stack.Count > 0 ? stack.Pop() : "nullptr";
                var key = stack.Count > 0 ? stack.Pop() : "0";
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ktmp = $"__dk{tempCounter++}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{keyCppType} {ktmp} = ({keyCppType}){key};"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::dict_try_get_value({dictArg}, &{ktmp}, {valueOut});"
                });
                stack.Push(tmp);
                return true;
            }

            case "Clear":
            {
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::dict_clear({dictArg});"
                });
                return true;
            }

            case "Add":
            {
                // Add(key, value) — same as set_Item but throws on duplicate
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var key = stack.Count > 0 ? stack.Pop() : "0";
                var dictArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var ktmp = $"__dk{tempCounter++}";
                var vtmp = $"__dv{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{{ {keyCppType} {ktmp} = ({keyCppType}){key}; {valCppType} {vtmp} = ({valCppType}){value}; cil2cpp::dict_set({dictArg}, &{ktmp}, &{vtmp}); }}"
                });
                return true;
            }

            default:
                return false;
        }
    }

    // ===== Helper: Get TypeInfo expression for a type reference =====

    /// <summary>
    /// Get C++ expression for the TypeInfo pointer of a type reference.
    /// For primitive types, uses their mangled TypeInfo name.
    /// For user types, uses the generated TypeInfo name.
    /// </summary>
    private string GetTypeInfoExpr(TypeReference typeRef)
    {
        var mangledName = GetMangledTypeNameForRef(typeRef);
        return $"&{mangledName}_TypeInfo";
    }
}
