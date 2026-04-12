#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // Shim for C# 9 "init" and record support on frameworks that don't define this type (e.g. netstandard2.0).
    // Internal is sufficient for compiler usage and avoids leaking the type publicly.
    internal static class IsExternalInit { }
}
#endif
