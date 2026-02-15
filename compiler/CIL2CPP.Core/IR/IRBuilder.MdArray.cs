using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Multi-dimensional array (T[,], T[,,], etc.) interception.
/// Intercepts .ctor, Get, Set, Address calls on ArrayType with Rank > 1.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Detect if a type is a multi-dimensional array (rank >= 2).
    /// </summary>
    private static bool IsMdArrayType(TypeReference typeRef)
        => typeRef is ArrayType at && at.Rank > 1;

    /// <summary>
    /// Try to intercept newobj on a multi-dimensional array.
    /// IL: newobj instance void int32[0...,0...]::.ctor(int32, int32)
    /// </summary>
    private bool TryEmitMdArrayNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsMdArrayType(ctorRef.DeclaringType))
            return false;

        var arrayType = (ArrayType)ctorRef.DeclaringType;
        int rank = arrayType.Rank;
        var elemType = arrayType.ElementType;
        var elemCppType = CppNameMapper.MangleTypeName(elemType.FullName);

        // Ensure TypeInfo exists for primitive element types
        if (CppNameMapper.IsPrimitive(elemType.FullName))
            _module.RegisterPrimitiveTypeInfo(elemType.FullName);

        // Pop dimension lengths from stack (in reverse order)
        var dims = new List<string>();
        for (int i = 0; i < rank; i++)
            dims.Add(stack.Count > 0 ? stack.Pop() : "0");
        dims.Reverse();

        var tmp = $"__t{tempCounter++}";
        var dimsInit = string.Join(", ", dims);

        // Emit: int32_t __mdims_N[] = { dim0, dim1, ... };
        // Then: auto __tN = cil2cpp::mdarray_create(&Elem_TypeInfo, rank, __mdims_N);
        var dimsVar = $"__mdims_{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {dimsVar}[] = {{ {dimsInit} }};"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = (cil2cpp::MdArray*)cil2cpp::mdarray_create(&{elemCppType}_TypeInfo, {rank}, {dimsVar});"
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Try to intercept call/callvirt on a multi-dimensional array method.
    /// Methods: Get, Set, Address on ArrayType with Rank > 1.
    /// </summary>
    private bool TryEmitMdArrayCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsMdArrayType(methodRef.DeclaringType))
            return false;

        var arrayType = (ArrayType)methodRef.DeclaringType;
        int rank = arrayType.Rank;
        var elemType = arrayType.ElementType;
        var elemCppType = CppNameMapper.GetCppTypeName(elemType.FullName);

        switch (methodRef.Name)
        {
            case "Get":
                return EmitMdArrayGet(block, stack, rank, elemCppType, ref tempCounter);
            case "Set":
                return EmitMdArraySet(block, stack, rank, elemCppType, ref tempCounter);
            case "Address":
                return EmitMdArrayAddress(block, stack, rank, elemCppType, ref tempCounter);
            default:
                return false;
        }
    }

    /// <summary>
    /// Emit multi-dim array element load: arr.Get(i, j, ...)
    /// IL: call instance T T[0...,0...]::Get(int32, int32, ...)
    /// </summary>
    private static bool EmitMdArrayGet(IRBasicBlock block, Stack<string> stack,
        int rank, string elemCppType, ref int tempCounter)
    {
        // Pop indices (reverse order), then 'this'
        var indices = PopIndices(stack, rank);
        var arr = stack.Count > 0 ? stack.Pop() : "nullptr";

        var idxVar = $"__mdidx_{tempCounter++}";
        var tmp = $"__t{tempCounter++}";
        var idxInit = string.Join(", ", indices);

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {idxVar}[] = {{ {idxInit} }};"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = *static_cast<{elemCppType}*>(cil2cpp::mdarray_get_element_ptr((cil2cpp::MdArray*){arr}, {idxVar}));"
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Emit multi-dim array element store: arr.Set(i, j, ..., value)
    /// IL: call instance void T[0...,0...]::Set(int32, int32, ..., T)
    /// Note: Set has rank indices + 1 value parameter.
    /// </summary>
    private static bool EmitMdArraySet(IRBasicBlock block, Stack<string> stack,
        int rank, string elemCppType, ref int tempCounter)
    {
        // Pop value first (last parameter), then indices, then 'this'
        var value = stack.Count > 0 ? stack.Pop() : "0";
        var indices = PopIndices(stack, rank);
        var arr = stack.Count > 0 ? stack.Pop() : "nullptr";

        var idxVar = $"__mdidx_{tempCounter++}";
        var idxInit = string.Join(", ", indices);

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {idxVar}[] = {{ {idxInit} }};"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"*static_cast<{elemCppType}*>(cil2cpp::mdarray_get_element_ptr((cil2cpp::MdArray*){arr}, {idxVar})) = {value};"
        });
        return true;
    }

    /// <summary>
    /// Emit multi-dim array element address: ref arr[i, j, ...]
    /// IL: call instance T& T[0...,0...]::Address(int32, int32, ...)
    /// </summary>
    private static bool EmitMdArrayAddress(IRBasicBlock block, Stack<string> stack,
        int rank, string elemCppType, ref int tempCounter)
    {
        var indices = PopIndices(stack, rank);
        var arr = stack.Count > 0 ? stack.Pop() : "nullptr";

        var idxVar = $"__mdidx_{tempCounter++}";
        var tmp = $"__t{tempCounter++}";
        var idxInit = string.Join(", ", indices);

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {idxVar}[] = {{ {idxInit} }};"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = static_cast<{elemCppType}*>(cil2cpp::mdarray_get_element_ptr((cil2cpp::MdArray*){arr}, {idxVar}));"
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Pop N index values from the stack in the correct order.
    /// </summary>
    private static List<string> PopIndices(Stack<string> stack, int count)
    {
        var indices = new List<string>();
        for (int i = 0; i < count; i++)
            indices.Add(stack.Count > 0 ? stack.Pop() : "0");
        indices.Reverse();
        return indices;
    }
}
