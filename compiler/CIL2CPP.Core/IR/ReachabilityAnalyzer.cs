using Mono.Cecil;
using Mono.Cecil.Cil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Result of reachability analysis: the sets of reachable types and methods.
/// </summary>
public class ReachabilityResult
{
    public HashSet<TypeDefinition> ReachableTypes { get; } = new();
    public HashSet<MethodDefinition> ReachableMethods { get; } = new();

    public bool IsReachable(TypeDefinition type) => ReachableTypes.Contains(type);
    public bool IsReachable(MethodDefinition method) => ReachableMethods.Contains(method);
}

/// <summary>
/// Performs reachability analysis (tree shaking) starting from entry point(s).
/// Computes the set of types and methods that are actually reachable,
/// so unreachable BCL/library types can be excluded from compilation.
/// </summary>
public class ReachabilityAnalyzer
{
    private readonly AssemblySet _assemblySet;
    private readonly ReachabilityResult _result = new();
    private readonly Queue<MethodDefinition> _worklist = new();
    private readonly HashSet<string> _processedMethods = new();

    public ReachabilityAnalyzer(AssemblySet assemblySet)
    {
        _assemblySet = assemblySet;
    }

    /// <summary>
    /// Run the reachability analysis and return the result.
    /// </summary>
    public ReachabilityResult Analyze()
    {
        // Seed: find the entry point
        var entryPoint = _assemblySet.RootAssembly.EntryPoint;
        if (entryPoint != null)
        {
            SeedMethod(entryPoint);
        }
        else
        {
            // Library mode: seed all public types/methods
            SeedAllPublicTypes(_assemblySet.RootAssembly);
        }

        // Worklist fixpoint
        while (_worklist.Count > 0)
        {
            var method = _worklist.Dequeue();
            ProcessMethod(method);
        }

        return _result;
    }

    private void SeedMethod(MethodDefinition method)
    {
        if (!_result.ReachableMethods.Add(method))
            return;

        MarkTypeReachable(method.DeclaringType);
        _worklist.Enqueue(method);
    }

    private void SeedAllPublicTypes(AssemblyDefinition assembly)
    {
        foreach (var type in assembly.MainModule.Types)
        {
            if (type.Name == "<Module>") continue;
            if (!type.IsPublic) continue;

            MarkTypeReachable(type);
            foreach (var method in type.Methods)
            {
                if (method.IsPublic || method.IsFamily)
                    SeedMethod(method);
            }
        }
    }

    private void MarkTypeReachable(TypeDefinition type)
    {
        if (!_result.ReachableTypes.Add(type))
            return;

        // Mark base types reachable (needed for struct layout / vtable)
        if (type.BaseType != null)
        {
            var baseTypeDef = TryResolve(type.BaseType);
            if (baseTypeDef != null)
                MarkTypeReachable(baseTypeDef);
        }

        // Mark interface types reachable
        foreach (var iface in type.Interfaces)
        {
            var ifaceDef = TryResolve(iface.InterfaceType);
            if (ifaceDef != null)
                MarkTypeReachable(ifaceDef);
        }

        // Mark static constructor reachable
        var cctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic);
        if (cctor != null)
            SeedMethod(cctor);

        // Conservative: mark all methods of the type reachable
        foreach (var method in type.Methods)
        {
            SeedMethod(method);
        }

        // Mark field types reachable
        foreach (var field in type.Fields)
        {
            var fieldTypeDef = TryResolve(field.FieldType);
            if (fieldTypeDef != null)
                MarkTypeReachable(fieldTypeDef);
        }

        // Process nested types
        foreach (var nested in type.NestedTypes)
        {
            // Nested types of a reachable type are reachable
            // (they may be compiler-generated closures, state machines, etc.)
            MarkTypeReachable(nested);
        }
    }

    private void ProcessMethod(MethodDefinition method)
    {
        var key = GetMethodKey(method);
        if (!_processedMethods.Add(key))
            return;

        if (!method.HasBody)
            return;

        foreach (var instr in method.Body.Instructions)
        {
            switch (instr.OpCode.Code)
            {
                // Method references
                case Code.Call:
                case Code.Callvirt:
                case Code.Newobj:
                case Code.Ldftn:
                case Code.Ldvirtftn:
                    ProcessMethodRef(instr.Operand as MethodReference);
                    break;

                // Type references
                case Code.Newarr:
                case Code.Castclass:
                case Code.Isinst:
                case Code.Box:
                case Code.Unbox:
                case Code.Unbox_Any:
                case Code.Initobj:
                case Code.Ldobj:
                case Code.Stobj:
                case Code.Ldelem_Any:
                case Code.Stelem_Any:
                case Code.Ldelema:
                case Code.Sizeof:
                case Code.Constrained:
                    ProcessTypeRef(instr.Operand as TypeReference);
                    break;

                // Field references
                case Code.Ldfld:
                case Code.Stfld:
                case Code.Ldsfld:
                case Code.Stsfld:
                case Code.Ldflda:
                case Code.Ldsflda:
                    ProcessFieldRef(instr.Operand as FieldReference);
                    break;

                // Ldtoken can be field, type, or method
                case Code.Ldtoken:
                    if (instr.Operand is MethodReference tokenMethod)
                        ProcessMethodRef(tokenMethod);
                    else if (instr.Operand is TypeReference tokenType)
                        ProcessTypeRef(tokenType);
                    else if (instr.Operand is FieldReference tokenField)
                        ProcessFieldRef(tokenField);
                    break;
            }
        }
    }

    private void ProcessMethodRef(MethodReference? methodRef)
    {
        if (methodRef == null) return;

        // Mark the declaring type reachable
        var declaringTypeDef = TryResolve(methodRef.DeclaringType);
        if (declaringTypeDef != null)
            MarkTypeReachable(declaringTypeDef);

        // Resolve and seed the target method
        var methodDef = TryResolveMethod(methodRef);
        if (methodDef != null)
            SeedMethod(methodDef);
    }

    private void ProcessTypeRef(TypeReference? typeRef)
    {
        if (typeRef == null) return;

        var typeDef = TryResolve(typeRef);
        if (typeDef != null)
            MarkTypeReachable(typeDef);
    }

    private void ProcessFieldRef(FieldReference? fieldRef)
    {
        if (fieldRef == null) return;

        var declaringTypeDef = TryResolve(fieldRef.DeclaringType);
        if (declaringTypeDef != null)
            MarkTypeReachable(declaringTypeDef);

        var fieldTypeDef = TryResolve(fieldRef.FieldType);
        if (fieldTypeDef != null)
            MarkTypeReachable(fieldTypeDef);
    }

    private TypeDefinition? TryResolve(TypeReference typeRef)
    {
        try
        {
            // Handle generic instances
            if (typeRef is GenericInstanceType git)
            {
                // Resolve the element type
                var elemDef = TryResolve(git.ElementType);
                // Mark generic argument types as reachable
                foreach (var arg in git.GenericArguments)
                {
                    var argDef = TryResolve(arg);
                    if (argDef != null)
                        MarkTypeReachable(argDef);
                }
                return elemDef;
            }

            // Handle array types
            if (typeRef is ArrayType arrayType)
            {
                var elemDef = TryResolve(arrayType.ElementType);
                if (elemDef != null)
                    MarkTypeReachable(elemDef);
                return null; // Arrays are handled by the runtime
            }

            // Handle by-ref and pointer types
            if (typeRef is ByReferenceType byRef)
                return TryResolve(byRef.ElementType);
            if (typeRef is PointerType ptr)
                return TryResolve(ptr.ElementType);

            // Skip generic parameters (T, U, etc.)
            if (typeRef is GenericParameter)
                return null;

            var resolved = typeRef.Resolve();
            if (resolved != null)
            {
                // Ensure the assembly is loaded in our set
                _assemblySet.LoadAssembly(resolved.Module.Assembly.Name.Name);
            }
            return resolved;
        }
        catch
        {
            return null;
        }
    }

    private MethodDefinition? TryResolveMethod(MethodReference methodRef)
    {
        try
        {
            // Handle generic instance methods
            if (methodRef is GenericInstanceMethod gim)
            {
                foreach (var arg in gim.GenericArguments)
                    TryResolve(arg);
                return TryResolveMethod(gim.ElementMethod);
            }

            return methodRef.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static string GetMethodKey(MethodDefinition method)
    {
        return $"{method.DeclaringType.FullName}::{method.FullName}";
    }
}
