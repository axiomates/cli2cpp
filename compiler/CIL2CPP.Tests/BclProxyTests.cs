using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("SampleAssembly")]
public class BclProxyTests
{
    private readonly SampleAssemblyFixture _fixture;

    public BclProxyTests(SampleAssemblyFixture fixture)
    {
        _fixture = fixture;
    }

    private IRModule BuildFeatureTest(BuildConfiguration? config = null)
    {
        using var reader = new AssemblyReader(_fixture.FeatureTestDllPath, config);
        var builder = new IRBuilder(reader, config);
        return builder.Build();
    }

    // ===== Non-Generic BCL Interface Proxies =====

    [Fact]
    public void BclProxy_IDisposable_Created()
    {
        var module = BuildFeatureTest();
        var iDisposable = module.Types.FirstOrDefault(t => t.ILFullName == "System.IDisposable");
        Assert.NotNull(iDisposable);
        Assert.True(iDisposable.IsInterface);
        Assert.True(iDisposable.IsAbstract);
    }

    [Fact]
    public void BclProxy_IDisposable_HasDisposeMethod()
    {
        var module = BuildFeatureTest();
        var iDisposable = module.Types.First(t => t.ILFullName == "System.IDisposable");
        var dispose = iDisposable.Methods.FirstOrDefault(m => m.Name == "Dispose");
        Assert.NotNull(dispose);
        Assert.True(dispose.IsVirtual);
        Assert.True(dispose.IsAbstract);
        Assert.Equal("void", dispose.ReturnTypeCpp);
        Assert.Empty(dispose.Parameters);
    }

    [Fact]
    public void BclProxy_IDisposable_CppNameMangled()
    {
        var module = BuildFeatureTest();
        var iDisposable = module.Types.First(t => t.ILFullName == "System.IDisposable");
        Assert.Equal("System_IDisposable", iDisposable.CppName);
    }

    [Fact]
    public void BclProxy_IComparable_Created()
    {
        var module = BuildFeatureTest();
        var iComparable = module.Types.FirstOrDefault(t => t.ILFullName == "System.IComparable");
        Assert.NotNull(iComparable);
        Assert.True(iComparable.IsInterface);
    }

    [Fact]
    public void BclProxy_IComparable_HasCompareToMethod()
    {
        var module = BuildFeatureTest();
        var iComparable = module.Types.First(t => t.ILFullName == "System.IComparable");
        var compareTo = iComparable.Methods.FirstOrDefault(m => m.Name == "CompareTo");
        Assert.NotNull(compareTo);
        Assert.Single(compareTo.Parameters);
        Assert.Equal("System.Object", compareTo.Parameters[0].ILTypeName);
    }

    // ===== User Types Resolve BCL Interfaces =====

    [Fact]
    public void BclProxy_ManagedResource_ImplementsIDisposable()
    {
        var module = BuildFeatureTest();
        var resource = module.Types.FirstOrDefault(t => t.Name == "ManagedResource");
        Assert.NotNull(resource);
        Assert.Contains(resource.Interfaces, i => i.ILFullName == "System.IDisposable");
    }

    [Fact]
    public void BclProxy_Priority_ImplementsIComparable()
    {
        var module = BuildFeatureTest();
        var priority = module.Types.FirstOrDefault(t => t.Name == "Priority");
        Assert.NotNull(priority);
        Assert.Contains(priority.Interfaces, i => i.ILFullName == "System.IComparable");
    }

    // ===== Interface Implementation Maps =====

    [Fact]
    public void BclProxy_ManagedResource_HasInterfaceImpl()
    {
        var module = BuildFeatureTest();
        var resource = module.Types.First(t => t.Name == "ManagedResource");

        // Should have at least one InterfaceImpl for IDisposable
        var disposableImpl = resource.InterfaceImpls
            .FirstOrDefault(impl => impl.Interface.ILFullName == "System.IDisposable");
        Assert.NotNull(disposableImpl);
    }

    [Fact]
    public void BclProxy_ManagedResource_InterfaceImplMapsDispose()
    {
        var module = BuildFeatureTest();
        var resource = module.Types.First(t => t.Name == "ManagedResource");

        var disposableImpl = resource.InterfaceImpls
            .First(impl => impl.Interface.ILFullName == "System.IDisposable");

        // Dispose method should be mapped (slot 0)
        Assert.Single(disposableImpl.MethodImpls);
        Assert.NotNull(disposableImpl.MethodImpls[0]);
        Assert.Equal("Dispose", disposableImpl.MethodImpls[0]!.Name);
    }

    [Fact]
    public void BclProxy_Priority_InterfaceImplMapsCompareTo()
    {
        var module = BuildFeatureTest();
        var priority = module.Types.First(t => t.Name == "Priority");

        var comparableImpl = priority.InterfaceImpls
            .First(impl => impl.Interface.ILFullName == "System.IComparable");

        Assert.Single(comparableImpl.MethodImpls);
        Assert.NotNull(comparableImpl.MethodImpls[0]);
        Assert.Equal("CompareTo", comparableImpl.MethodImpls[0]!.Name);
    }

    // ===== Proxy Properties =====

    [Fact]
    public void BclProxy_NotRuntimeProvided()
    {
        var module = BuildFeatureTest();
        var iDisposable = module.Types.First(t => t.ILFullName == "System.IDisposable");
        // BCL proxy interfaces need their own TypeInfo emitted — NOT runtime-provided
        Assert.False(iDisposable.IsRuntimeProvided);
    }

    [Fact]
    public void BclProxy_NoFields()
    {
        var module = BuildFeatureTest();
        var iDisposable = module.Types.First(t => t.ILFullName == "System.IDisposable");
        Assert.Empty(iDisposable.Fields);
        Assert.Empty(iDisposable.StaticFields);
    }

    // ===== Only Created When Referenced =====

    [Fact]
    public void BclProxy_UnreferencedInterfacesNotCreated()
    {
        // ICloneable is not implemented by any type in FeatureTest
        var module = BuildFeatureTest();
        var cloneable = module.Types.FirstOrDefault(t => t.ILFullName == "System.ICloneable");
        Assert.Null(cloneable);
    }
}
