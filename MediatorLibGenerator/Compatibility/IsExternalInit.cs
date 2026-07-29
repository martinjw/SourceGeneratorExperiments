// Shim for C# 9 "init" and record support on frameworks that don't define this type (e.g. netstandard2.0).
// The compiler looks for System.Runtime.CompilerServices.IsExternalInit specifically, so the type
// must live in that namespace. Keeping it internal avoids exposing it publicly.
namespace System.Runtime.CompilerServices
{
    // Public so the compiler can see it when resolving predefined types used for "init" accessors.
    public static class IsExternalInit { }
}
