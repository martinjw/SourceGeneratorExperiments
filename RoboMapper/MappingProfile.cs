using System.Collections.Generic;

namespace RoboMapper;

/// <summary>
/// Provides a base class for defining object-to-object mapping profiles.
/// </summary>
/// <remarks>Inherit from this class to configure mapping rules between source and destination types.
/// Mapping profiles are typically used to centralize and organize mapping configurations in applications that
/// require object transformation.</remarks>
public abstract class MappingProfile
{
    internal List<IMappingExpression> Mappings { get; } = new();

    protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        var expr = new MappingExpression<TSource, TDestination>();
        Mappings.Add(expr);
        return expr;
    }
}