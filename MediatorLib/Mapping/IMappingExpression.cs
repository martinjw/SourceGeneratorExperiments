namespace MediatorLib.Mapping
{
    public interface IMemberOptions<TSource>
    {
        void MapFrom(Func<TSource, object?> mapFrom);
    }

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
        /// identifies the member in the destination type, and the <paramref name="mapFrom"/> parameter defines how to map the value from the source type.
        /// </summary>
        /// <param name="destinationMember">The name of the member in the destination type to which the value will be mapped.</param>
        /// <param name="mapFrom">A function that defines how to map the value from the source type to the destination member.</param>
        /// <returns>The current mapping expression, allowing for fluent configuration of additional member mappings.</returns>
        IMappingExpression<TSource, TDestination> ForMember(
            string destinationMember,
            Func<TSource, object?> mapFrom);

        /// <summary>
        /// Specifies mapping rules for a specific member of the destination type, allowing for detailed configuration of how the member is populated from the source type.
        /// </summary>
        /// <typeparam name="TMember">The type of the member in the destination type.</typeparam>
        /// <param name="destinationMember">An expression that identifies the member in the destination type.</param>
        /// <param name="memberOptions">An action that configures the mapping options for the member.</param>
        /// <returns>The current mapping expression, allowing for fluent configuration of additional member mappings.</returns>
        IMappingExpression<TSource, TDestination> ForMember<TMember>(
            System.Linq.Expressions.Expression<Func<TDestination, TMember>> destinationMember,
            Action<IMemberOptions<TSource>> memberOptions);

        /// <summary>
        /// Creates a mapping in the reverse direction, from the destination type to the source type.
        /// </summary>
        /// <remarks>Use this method to automatically generate a reverse mapping based on the existing
        /// configuration. This is useful when you need to map objects in both directions without manually configuring each
        /// mapping.</remarks>
        /// <returns>An object that allows further configuration of the reverse mapping between the destination and source types.</returns>
        IMappingExpression<TDestination, TSource> ReverseMap();

        /// <summary>
        /// Executes a custom action after the mapping from source to destination has been completed.
        /// </summary>
        /// <param name="afterAction">An action that receives both the source and destination objects after the initial mapping is complete.</param>
        /// <returns>The current mapping expression, allowing for fluent configuration of additional mapping rules.</returns>
        IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> afterAction);
    }
}