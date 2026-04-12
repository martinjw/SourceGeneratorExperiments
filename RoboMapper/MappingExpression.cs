using System;
using System.Collections.Generic;

namespace RoboMapper;

/// <summary>
/// Defines a mapping configuration between a source type and a destination type, allowing customization of how
/// individual members are mapped.
/// </summary>
/// <remarks>This is only used as a configuration DSL; the generator reads it, not executes it.</remarks>
/// <typeparam name="TSource">The type of the source object to map from.</typeparam>
/// <typeparam name="TDestination">The type of the destination object to map to.</typeparam>
public sealed class MappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
{
    internal List<(string DestMember, string SourceMember)> CustomMappings { get; } = new();
    internal bool Reverse { get; private set; }

    /// <summary>
    /// Maps a specific member of the destination type to a member of the source type using a provided mapping function.
    /// </summary>
    /// <param name="destinationMember">The name of the member in the destination type to which the value will be mapped.</param>
    /// <param name="mapFrom">A function that defines how to map the value from the source type to the destination member.</param>
    /// <returns>The current mapping expression, allowing for fluent configuration of additional member mappings.</returns>
    public IMappingExpression<TSource, TDestination> ForMember(
        string destinationMember,
        Func<TSource, object?> mapFrom)
    {
        // We only care about the method name of the lambda target.
        CustomMappings.Add((destinationMember, mapFrom.Method.Name));
        return this;
    }

    /// <summary>
    /// Reverses the mapping configuration, allowing you to define how to map from the destination type back to the source type.
    /// </summary>
    /// <returns>An object that allows further configuration of the reverse mapping between the destination and source types.</returns>
    public IMappingExpression<TDestination, TSource> ReverseMap()
    {
        Reverse = true;
        return new MappingExpression<TDestination, TSource>();
    }
}