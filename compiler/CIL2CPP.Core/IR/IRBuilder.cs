using Mono.Cecil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Converts IL (from Mono.Cecil) into IR representation.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Well-known vtable slot indices for System.Object virtual methods.
    /// These must match the order used in BuildVTable root seeding and EmitMethodCall.
    /// </summary>
    private static class ObjectVTableSlots
    {
        public const int ToStringSlot = 0;
        public const int EqualsSlot = 1;
        public const int GetHashCodeSlot = 2;
    }

    private readonly AssemblyReader _reader;
    private readonly IRModule _module;
    private readonly BuildConfiguration _config;
    private readonly Dictionary<string, IRType> _typeCache = new();

    // Generic type instantiation tracking
    private readonly Dictionary<string, GenericInstantiationInfo> _genericInstantiations = new();

    private record GenericInstantiationInfo(
        string OpenTypeName,
        List<string> TypeArguments,
        string MangledName,
        TypeDefinition? CecilOpenType
    );

    // Generic method instantiation tracking
    private readonly Dictionary<string, GenericMethodInstantiationInfo> _genericMethodInstantiations = new();

    private record GenericMethodInstantiationInfo(
        string DeclaringTypeName,
        string MethodName,
        List<string> TypeArguments,
        string MangledName,
        MethodDefinition CecilMethod
    );

    public IRBuilder(AssemblyReader reader, BuildConfiguration? config = null)
    {
        _reader = reader;
        _config = config ?? BuildConfiguration.Release;
        _module = new IRModule { Name = reader.AssemblyName };
    }

    /// <summary>
    /// Build the complete IR module from the assembly.
    /// </summary>
    public IRModule Build()
    {
        CppNameMapper.ClearValueTypes();

        // Pass 0: Scan for generic instantiations in all method bodies
        ScanGenericInstantiations();

        // Pass 1: Create all type shells (no fields/methods yet)
        // Skip open generic types — they are templates, not concrete types
        foreach (var typeDef in _reader.GetAllTypes())
        {
            if (typeDef.HasGenericParameters)
                continue;
            var irType = CreateTypeShell(typeDef);
            _module.Types.Add(irType);
            _typeCache[typeDef.FullName] = irType;
        }

        // Pass 1.5: Create specialized types for each generic instantiation
        CreateGenericSpecializations();

        // Pass 2: Fill in fields, base types, interfaces
        foreach (var typeDef in _reader.GetAllTypes())
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                PopulateTypeDetails(typeDef, irType);
            }
        }

        // Pass 2.5: Flag types with static constructors (before method conversion)
        foreach (var typeDef in _reader.GetAllTypes())
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType2))
            {
                irType2.HasCctor = typeDef.Methods.Any(m => m.IsConstructor && m.IsStatic);
            }
        }

        // Pass 3: Create method shells (no body yet — needed for VTable)
        // Skip open generic methods — they are templates, specialized in Pass 3.5
        var methodBodies = new List<(IL.MethodInfo MethodDef, IRMethod IRMethod)>();
        foreach (var typeDef in _reader.GetAllTypes())
        {
            if (typeDef.HasGenericParameters) continue;
            if (_typeCache.TryGetValue(typeDef.FullName, out var irType))
            {
                foreach (var methodDef in typeDef.Methods)
                {
                    // Skip open generic methods — they'll be monomorphized in Pass 3.5
                    if (methodDef.HasGenericParameters) continue;

                    var irMethod = ConvertMethod(methodDef, irType);
                    irType.Methods.Add(irMethod);

                    // Detect entry point
                    if (methodDef.Name == "Main" && methodDef.IsStatic)
                    {
                        irMethod.IsEntryPoint = true;
                        _module.EntryPoint = irMethod;
                    }

                    // Track finalizer
                    if (irMethod.IsFinalizer)
                        irType.Finalizer = irMethod;

                    // Save for body conversion later (skip abstract and InternalCall)
                    if (methodDef.HasBody && !methodDef.IsAbstract && !irMethod.IsInternalCall)
                        methodBodies.Add((methodDef, irMethod));
                }
            }
        }

        // Pass 3.5: Create specialized methods for each generic method instantiation
        CreateGenericMethodSpecializations();

        // Pass 4: Build vtables (needs method shells with IsVirtual)
        foreach (var irType in _module.Types)
        {
            BuildVTable(irType);
        }

        // Pass 5: Build interface implementation maps
        foreach (var irType in _module.Types)
        {
            if (!irType.IsInterface && !irType.IsValueType)
                BuildInterfaceImpls(irType);
        }

        // Pass 6: Convert method bodies (vtables are now available for virtual dispatch)
        foreach (var (methodDef, irMethod) in methodBodies)
        {
            ConvertMethodBody(methodDef, irMethod);
        }

        return _module;
    }
}
