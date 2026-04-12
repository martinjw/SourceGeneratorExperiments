using System;

namespace RoboMapper;

/// <summary>
/// Defines a contract for configuring object mapping expressions.
/// </summary>
/// <remarks>Implementations of this interface provide mechanisms for specifying how objects are mapped
/// from one type to another. This interface is typically used in mapping frameworks to enable fluent configuration
/// of mapping rules.</remarks>
public interface IMappingExpression { }

/// <summary>
/// Defines a mapping configuration between a source type and a destination type, allowing customization of how
/// individual members are mapped.
/// </summary>
/// <remarks>Use this interface to configure member-level mapping rules or to enable reverse mapping
/// between types. Typically used in object mapping scenarios to control how data is transferred between different
/// object models.</remarks>
/// <typeparam name="TSource">The type of the source object to map from.</typeparam>
/// <typeparam name="TDestination">The type of the destination object to map to.</typeparam>
public interface IMappingExpression<TSource, TDestination> : IMappingExpression
{
    /// <summary>
    /// Specify mapping rules for a specific member of the destination type. The <paramref name="destinationMember"/> parameter
    /// </summary>
    /// <param name="destinationMember"></param>
    /// <param name="mapFrom"></param>
    /// <returns></returns>
    IMappingExpression<TSource, TDestination> ForMember(
        string destinationMember,
        Func<TSource, object?> mapFrom);

    /// <summary>
    /// Creates a mapping in the reverse direction, from the destination type to the source type.
    /// </summary>
    /// <remarks>Use this method to automatically generate a reverse mapping based on the existing
    /// configuration. This is useful when you need to map objects in both directions without manually configuring each
    /// mapping.</remarks>
    /// <returns>An object that allows further configuration of the reverse mapping between the destination and source types.</returns>
    IMappingExpression<TDestination, TSource> ReverseMap();
}