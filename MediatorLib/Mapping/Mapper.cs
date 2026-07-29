namespace MediatorLib.Mapping
{
    /// <summary>
    /// Provides functionality to map objects from one type to another using configured mappings.
    /// </summary>
    public sealed class Mapper : IMapper
    {
        /// <summary>
        /// Maps the specified source object to a new instance of the specified destination type.
        /// </summary>
        /// <remarks>This method uses the configured mappings to convert the source object to the specified
        /// destination type. Ensure that a mapping exists between the source and destination types before calling this
        /// method.</remarks>
        /// <typeparam name="TDestination">The type to which the source object is mapped.</typeparam>
        /// <param name="source">The object to map to the destination type. Cannot be null.</param>
        /// <returns>An instance of type TDestination with values mapped from the source object.</returns>
        /// <exception cref="ArgumentNullException">Thrown if the source parameter is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if no mapping is configured between the source object's type and the destination type.</exception>
        public TDestination Map<TDestination>(object source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            var srcType = source.GetType();
            var destType = typeof(TDestination);
            if (srcType == destType)
            {
                return (TDestination)source;
            }

            // Check if we have a direct mapping
            if (MapperConfiguration.Instance.TryGetMap(srcType, destType, out var del) && del is not null)
            {
                return ((Func<object, TDestination>)del)(source);
            }

            // Check if source is an IEnumerable and destination expects an IEnumerable
            if (TryMapEnumerable(source, srcType, destType, out var mappedResult))
            {
                return (TDestination)mappedResult;
            }

#if DEBUG
            var srcTypes = MapperConfiguration.Instance.SourceTypes
                .OrderBy(x => x.Key.FullName)
                .Select(kvp => $"Source: {kvp.Key.FullName} -> Destination: {kvp.Value.FullName}")
                .ToList();
            System.Diagnostics.Trace.TraceWarning("Configured source types for mapping:\n" + string.Join("\n", srcTypes));
#endif
            throw new InvalidOperationException($"No mapping from {srcType} to {destType}.");

        }

        private bool TryMapEnumerable(object source, Type srcType, Type destType, out object? result)
        {
            result = null;

            // Check if source implements IEnumerable (but not string)
            if (srcType == typeof(string) || !typeof(System.Collections.IEnumerable).IsAssignableFrom(srcType))
            {
                return false;
            }

            // Get the element type from the source
            var sourceElementType = GetEnumerableElementType(srcType);
            if (sourceElementType == null)
            {
                return false;
            }

            // Get the element type from the destination
            var destElementType = GetEnumerableElementType(destType);
            if (destElementType == null)
            {
                return false;
            }

            // Check if there's a mapping for the element types
            if (!MapperConfiguration.Instance.TryGetMap(sourceElementType, destElementType, out var elementMap) || elementMap is null)
            {
                return false;
            }

            // Map each element
            var mappedItems = new List<object>();
            foreach (var item in (System.Collections.IEnumerable)source)
            {
                if (item != null)
                {
                    var mapMethod = elementMap.GetType().GetMethod("Invoke");
                    var mappedItem = mapMethod?.Invoke(elementMap, new[] { item });
                    if (mappedItem != null)
                    {
                        mappedItems.Add(mappedItem);
                    }
                }
            }

            // Convert to the appropriate destination type
            result = ConvertToDestinationType(mappedItems, destType, destElementType);
            return result != null;
        }

        private Type? GetEnumerableElementType(Type type)
        {
            // Check if it's an array
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            // Check if it's a generic IEnumerable<T>
            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(IList<>) ||
                    genericTypeDefinition == typeof(List<>) ||
                    genericTypeDefinition == typeof(ICollection<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            // Check implemented interfaces for IEnumerable<T>
            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments()[0];
        }

        private object? ConvertToDestinationType(List<object> mappedItems, Type destType, Type destElementType)
        {
            // Create a strongly-typed list
            var listType = typeof(List<>).MakeGenericType(destElementType);
            var list = Activator.CreateInstance(listType) as System.Collections.IList;

            if (list == null)
            {
                return null;
            }

            foreach (var item in mappedItems)
            {
                list.Add(item);
            }

            // If destination is an array
            if (destType.IsArray)
            {
                var array = Array.CreateInstance(destElementType, list.Count);
                list.CopyTo(array, 0);
                return array;
            }

            // If destination is List<T> or IList<T> or IEnumerable<T> or ICollection<T>
            if (destType.IsGenericType)
            {
                var genericTypeDefinition = destType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(List<>) ||
                    genericTypeDefinition == typeof(IList<>) ||
                    genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(ICollection<>))
                {
                    return list;
                }
            }

            // If destination is IList or IEnumerable (non-generic), return the list
            if (destType == typeof(System.Collections.IList) ||
                destType == typeof(System.Collections.IEnumerable) ||
                destType == typeof(System.Collections.ICollection))
            {
                return list;
            }

            // Check if the destination type is assignable from List<T>
            if (destType.IsAssignableFrom(listType))
            {
                return list;
            }

            return null;
        }
    }
}