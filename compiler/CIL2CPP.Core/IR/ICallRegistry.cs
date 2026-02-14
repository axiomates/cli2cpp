namespace CIL2CPP.Core.IR;

/// <summary>
/// Registry of [InternalCall] method mappings to C++ implementations.
/// Maps .NET BCL internal methods to their C++ runtime equivalents.
/// </summary>
public static class ICallRegistry
{
    private static readonly Dictionary<string, string> _registry = new();

    static ICallRegistry()
    {
        // System.Object
        Register("System.Object", "MemberwiseClone", 0, "cil2cpp::object_memberwise_clone");
        Register("System.Object", "GetType", 0, "cil2cpp::object_get_type");

        // System.String
        Register("System.String", "FastAllocateString", 1, "cil2cpp::string_fast_allocate");
        Register("System.String", "get_Length", 0, "cil2cpp::string_length");
        Register("System.String", "get_Chars", 1, "cil2cpp::string_get_chars");

        // System.Array
        Register("System.Array", "get_Length", 0, "cil2cpp::array_get_length");
        Register("System.Array", "get_Rank", 0, "cil2cpp::array_get_rank");
        Register("System.Array", "Copy", 5, "cil2cpp::array_copy");
        Register("System.Array", "Clear", 3, "cil2cpp::array_clear");
        Register("System.Array", "GetLength", 1, "cil2cpp::array_get_length_dim");

        // Note: System.Math methods are handled by MapBclMethod() which runs first
        // in EmitMethodCall, so no ICallRegistry entries needed for Math.

        // System.Environment
        Register("System.Environment", "get_NewLine", 0, "cil2cpp::icall::Environment_get_NewLine");
        Register("System.Environment", "get_TickCount", 0, "cil2cpp::icall::Environment_get_TickCount");
        Register("System.Environment", "get_TickCount64", 0, "cil2cpp::icall::Environment_get_TickCount64");
        Register("System.Environment", "get_ProcessorCount", 0, "cil2cpp::icall::Environment_get_ProcessorCount");

        // System.GC
        Register("System.GC", "Collect", 0, "cil2cpp::gc_collect");
        Register("System.GC", "SuppressFinalize", 1, "cil2cpp::gc_suppress_finalize");
        Register("System.GC", "KeepAlive", 1, "cil2cpp::gc_keep_alive");
        Register("System.GC", "_Collect", 2, "cil2cpp::gc_collect"); // internal variant

        // System.Buffer
        Register("System.Buffer", "Memmove", 3, "cil2cpp::icall::Buffer_Memmove");
        Register("System.Buffer", "BlockCopy", 5, "cil2cpp::icall::Buffer_BlockCopy");

        // System.Type
        Register("System.Type", "GetTypeFromHandle", 1, "cil2cpp::icall::Type_GetTypeFromHandle");

        // System.Threading.Monitor
        Register("System.Threading.Monitor", "Enter", 1, "cil2cpp::icall::Monitor_Enter");
        Register("System.Threading.Monitor", "Exit", 1, "cil2cpp::icall::Monitor_Exit");
        Register("System.Threading.Monitor", "ReliableEnter", 2, "cil2cpp::icall::Monitor_ReliableEnter");
        Register("System.Threading.Monitor", "Wait", 2, "cil2cpp::icall::Monitor_Wait");
        Register("System.Threading.Monitor", "Pulse", 1, "cil2cpp::icall::Monitor_Pulse");
        Register("System.Threading.Monitor", "PulseAll", 1, "cil2cpp::icall::Monitor_PulseAll");

        // Note: System.Threading.Interlocked methods are handled by MapBclMethod()
        // which dispatches overloads based on parameter types (i32 vs i64 vs Object).

        // System.Threading.Thread
        Register("System.Threading.Thread", "Sleep", 1, "cil2cpp::icall::Thread_Sleep");

        // System.Runtime.CompilerServices.RuntimeHelpers
        Register("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", 2,
            "cil2cpp::icall::RuntimeHelpers_InitializeArray");
        Register("System.Runtime.CompilerServices.RuntimeHelpers", "IsReferenceOrContainsReferences", 0,
            "cil2cpp::icall::RuntimeHelpers_IsReferenceOrContainsReferences");
    }

    /// <summary>
    /// Register an internal call mapping.
    /// </summary>
    public static void Register(string typeFullName, string methodName, int paramCount, string cppFunctionName)
    {
        var key = MakeKey(typeFullName, methodName, paramCount);
        _registry[key] = cppFunctionName;
    }

    /// <summary>
    /// Look up the C++ function name for an [InternalCall] method.
    /// Returns null if no mapping exists.
    /// </summary>
    public static string? Lookup(string typeFullName, string methodName, int paramCount)
    {
        var key = MakeKey(typeFullName, methodName, paramCount);
        return _registry.TryGetValue(key, out var cppName) ? cppName : null;
    }

    private static string MakeKey(string typeFullName, string methodName, int paramCount)
        => $"{typeFullName}::{methodName}/{paramCount}";
}
