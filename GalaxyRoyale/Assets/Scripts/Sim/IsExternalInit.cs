// Polyfill for C# 9 init-only setters + records under .NET Standard 2.1
// (Unity 6's default API compatibility level). Without this, positional
// records in SimEventBus.cs fail to compile.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
