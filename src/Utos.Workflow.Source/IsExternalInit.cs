// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// The marker the compiler requires to emit an <c>init</c> accessor — which every positional
    /// <c>record</c> in this package has. It ships in the framework from .NET 5, and these
    /// packages target netstandard2.0, so it is declared here instead.
    /// <para>
    /// Internal, deliberately. The type is matched by full name, not by identity, so two
    /// assemblies each declaring their own is fine; a <em>public</em> one would leak a
    /// <c>System.Runtime.CompilerServices</c> type out of a Utos package and collide with the
    /// framework's for anyone multi-targeting.
    /// </para>
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
