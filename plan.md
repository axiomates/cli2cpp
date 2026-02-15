# CIL2CPP Development Roadmap — Toward Unity IL2CPP Architecture

## Context

CIL2CPP has completed Phases 1-7 + Steps 8.1-8.3 and 9.1/9.3 with 1153 C# / 425 C++ / 23 integration tests passing. The project supports most C# language features, but has two categories of gaps:

1. **Architecture gap**: BCL calls go through 85+ ICallRegistry mappings + 28 TryEmit* interceptions instead of compiling BCL IL (unlike Unity IL2CPP)
2. **Feature/quality gaps**: 7 exception types, limited string methods, no System.IO/Net, missing LINQ operators, etc.

**Key insight**: Many feature gaps (string.Format, LINQ OrderBy, String.op_Equality) would be **automatically fixed** by compiling BCL IL. So architecture alignment is not just a theoretical goal — it directly unblocks features.

**Strategy**: Architecture first (BCL IL compilation), then feature expansion for things that need independent work.

---

## Current Gap Summary

| Category | Count | Examples | Fixed by BCL IL? |
|----------|-------|---------|------------------|
| Architecture gaps | 4 | string.Format, LINQ OrderBy, String.op_Equality | Yes |
| Quality gaps | 10 | 7 exception types, nested try/catch edge cases | Partially |
| Feature gaps | 7 | IAsyncEnumerable, SIMD, CancellationToken | No |
| Runtime gaps | 2 | System.IO, System.Net | No |
| AOT limitations | 9 | Reflection.Emit, dynamic, Assembly.Load | Never (by design) |

---

## Recommended Execution Order

```
Priority  Task                              Unlocks                          Effort
──────────────────────────────────────────────────────────────────────────────────
1. 9.1    Expand exception types (15+)      Better error handling            Small
2. 9.3    String.op_Equality                Fix pattern matching             Tiny
3. 8.1    Nullable/Index/Range from IL      Architecture proof-of-concept    Small
4. 8.2    Refine reachability analysis      Less code bloat                  Small
5. 8.3    Split ICallRegistry               Foundation for BCL IL            Small
6. 9.2    CancellationToken                 Complete async story             Medium
7. 8.5    LINQ from IL                      All LINQ operators               Medium
8. 8.4    Console/String/Math from IL       string.Format, interpolation     Large
9. 10.1   System.IO basics                  File I/O                         Medium
10. 10.2  IAsyncEnumerable                  await foreach                    Large
```

**Rationale**:
- Start with quick wins (9.1, 9.3) for immediate user value
- Then architecture proof-of-concept (8.1-8.3) to validate the approach
- CancellationToken (9.2) completes the async story
- LINQ from IL (8.5) is the highest-value BCL IL step
- Console/String (8.4) is the hardest BCL IL step, do later
- System.IO and IAsyncEnumerable are independent features, do when needed

---

## Phase 8: BCL IL Compilation (Architecture Alignment)

**Goal**: Compile BCL method bodies from IL instead of stubbing them. Only `[InternalCall]` methods use icalls.

```
Current:                                    Target (Phase 8):
  BCL calls → TryEmit*/ICallRegistry         BCL calls → normal IL→C++ compilation
           → C++ hand-written impls                   → [InternalCall] only use icalls
                                                      → C++ only implements lowest-level native API
```

### Current State

- ICallRegistry: ~40 true `[InternalCall]` + ~55 managed shortcuts (could compile from IL)
- TryEmit*: 4 can compile (Nullable/Index/Range/GetSubArray), 9 must intercept (Async/Thread/Span/etc.)
- ReachabilityAnalyzer: already method-level granularity (good foundation)
- BCL namespace whitelist: narrow, needs expansion

### ICallRegistry Classification

| Category | Count | Status |
|----------|-------|--------|
| TRUE `[InternalCall]` (Monitor, Interlocked, GC, Buffer, Thread) | ~40 | **Keep** — native primitives |
| Managed shortcuts (Console, ToString, Math, Delegate, Object) | ~55 | **Could compile from IL** |

### TryEmit* Classification

| Category | Types | Assessment |
|----------|-------|-----------|
| **Can compile** | Nullable\<T\>, Index, Range, GetSubArray | Simple value types, trivial IL |
| **Must intercept** | Task/Async, Thread, Type, Span, MdArray | Deep runtime/native ties |
| **Hybrid** | List\<T\>, Dictionary\<K,V\>, ValueTuple, EqualityComparer | Simple accessors can compile, complex ops need runtime |

### Step 8.1: Proof-of-Concept — Compile Nullable/Index/Range from IL

**Goal**: In MA mode, skip TryEmit* for these 3 types, let BCL IL compile normally.

**Why these first**: Simple value types, trivial IL bodies, no deep BCL dependencies.

**Changes**:
- `IRBuilder.Emit.cs`: Skip Nullable/Index/Range interceptions in MA mode
- `CppCodeGenerator.Source.cs`: Compile (not stub) these types' methods
- `ReachabilityAnalyzer.cs`: Ensure these types pass boundary filtering

**Validation**: `int? x = 42; arr[^1]; arr[1..3]` compiles from BCL IL in MA mode.

### Step 8.2: Refine Reachability Analysis

**Goal**: Remove conservative "mark all user-type methods" behavior, only mark called methods.

**Changes**:
- `ReachabilityAnalyzer.cs`: Remove blanket SeedMethod for user types, expand BCL namespace whitelist

### Step 8.3: Split ICallRegistry — True ICalls vs Managed Shortcuts

**Goal**: Separate the ~40 true `[InternalCall]` entries from the ~55 managed shortcuts. In MA mode, only true icalls are active; managed methods compile from IL.

**True ICalls (keep forever)**: Monitor.Enter/Exit, Interlocked.*, GC.Collect, Buffer.Memmove, Thread.Sleep, Object.GetType/MemberwiseClone, String.FastAllocateString, Type.GetTypeFromHandle

**Managed shortcuts (compile from IL in MA mode)**: Console.WriteLine, Math.Sqrt, String.Concat, Object.ToString, Int32.ToString, Delegate.Combine, Array.Copy

### Step 8.4: Compile Console/String/Math from IL

**Goal**: The managed shortcuts from Step 8.3 now compile from BCL IL. Add low-level icalls for the native boundary.

**New icalls needed**:
- `System.Number.FormatInt32/FormatDouble` → C++ number formatting
- `System.IO.ConsoleStream.Write/Read` → `write()`/`read()`
- `System.Text.Unicode.*` → UTF-16/UTF-8 conversion helpers

**New runtime files**:
- `runtime/src/bcl/icalls_number.cpp`
- `runtime/src/bcl/icalls_io.cpp`

**Risk**: High — Console stack has many layers (Console → TextWriter → StreamWriter → Stream).

### Step 8.5: LINQ from IL

**Goal**: Compile `System.Linq.Enumerable` extension methods from BCL IL. This automatically adds OrderBy, GroupBy, Join, Distinct, Skip, Take, etc.

**Prerequisites**: Step 8.1 + working IEnumerable/IEnumerator from BCL

**Changes**:
- Whitelist `System.Linq` in ReachabilityAnalyzer
- Remove LINQ TryEmit* interceptions in MA mode
- Handle LINQ's heavy generic instantiation

**Impact**: Massive feature unlock — all LINQ operators work automatically.

---

## Phase 9: Quality Hardening (Independent of BCL IL)

These gaps need work regardless of architecture — no BCL IL compilation will fix them.

### Step 9.1: Expand Exception Types

**Current**: 7 types (NullReference, IndexOutOfRange, InvalidCast, InvalidOperation, Overflow, Argument, ArgumentNull)

**Add**: NotSupportedException, FormatException, ArithmeticException, DivideByZeroException, KeyNotFoundException, ObjectDisposedException, TimeoutException, AggregateException, OperationCanceledException

**Changes**:
- `runtime/include/cil2cpp/exception.h`: Add C++ structs
- `runtime/src/exception/`: TypeInfo definitions
- `compiler/CIL2CPP.Core/IR/CppNameMapper.cs`: RuntimeExceptionTypeMap entries

### Step 9.2: CancellationToken / TaskCompletionSource

**Goal**: Complete the async story with cancellation support.

**Changes**:
- CancellationTokenSource: atomic bool + callback list
- CancellationToken: struct wrapping source reference
- TaskCompletionSource\<T\>: manual Task completion
- OperationCanceledException: new exception type (from Step 9.1)

### Step 9.3: String.op_Equality + Pattern Matching Fix

**Goal**: Fix string `switch` / `is "literal"` in single-assembly mode.

**Changes**: One ICallRegistry entry: `String.op_Equality` → `cil2cpp::string_equals`.

### Step 9.4: String.Format / Interpolation Support

**Two approaches**:
- A) Add `String.Format` runtime implementation (ICallRegistry mapping)
- B) Wait for Phase 8 BCL IL compilation (automatic fix)

If Phase 8 is progressing, skip this — it's free once Console/String compile from IL.

---

## Phase 10: New Feature Expansion

### Step 10.1: System.IO Basics

**Goal**: `File.ReadAllText`, `File.WriteAllText`, `File.Exists`, `Directory.Exists`

**Changes**:
- New runtime icalls: `fopen`/`fread`/`fwrite`/`stat` wrappers
- If Phase 8 is done: just add icalls, BCL IL compiles the rest
- If Phase 8 is not done: need TryEmit* interceptions (more work)

### Step 10.2: IAsyncEnumerable\<T\>

**Goal**: `await foreach` support

**Changes**: New compiler state machine handling (similar to existing async + yield, but combined)

### Step 10.3: More Reflection (GetMethods/GetFields)

**Goal**: Basic reflection queries for runtime type inspection

**Changes**: Expand TypeInfo with method/field arrays, add reflection API runtime functions

---

## Key Architectural Decisions

1. **SA mode stays as-is**: Single-assembly mode continues using ICallRegistry + TryEmit*. Changes only affect MA mode.
2. **Incremental bypass**: Each TryEmit* interception is individually toggleable in MA mode.
3. **True icalls are permanent**: Monitor, Interlocked, GC, Buffer, Thread.Sleep — these stay forever (same as Unity IL2CPP).
4. **BCL IL compilation is opt-in per type family**: Not a big-bang switch, but gradual expansion.

## Verification Strategy

After each step:
```bash
dotnet test compiler/CIL2CPP.Tests
ctest --test-dir runtime/tests/build -C Debug --output-on-failure
python tools/dev.py integration
# For Phase 8 steps: also test MA mode
dotnet run --project compiler/CIL2CPP.CLI -- codegen \
    -i compiler/samples/HelloWorld/HelloWorld.csproj \
    -o output/ma --multi-assembly
```
