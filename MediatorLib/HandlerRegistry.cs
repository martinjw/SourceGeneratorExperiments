using System.Reflection;

namespace MediatorLib
{
    /// <summary>
    /// A reflection-based registry that scans provided assemblies to discover and register request and notification handlers for a mediator pattern implementation.
    /// </summary>
    public class HandlerRegistry : IHandlerRegistry
    {
        public Dictionary<Type, Type> RequestHandlers { get; } = new();
        public Dictionary<Type, List<Type>> NotificationHandlers { get; } = new();

        /// <summary>
        /// Initializes a new instance of the HandlerRegistry class by scanning the provided assemblies for handler
        /// types.
        /// </summary>
        /// <remarks>This constructor populates the registry with handler types discovered in the
        /// specified assemblies. Only types implementing IRequestHandler or INotificationHandler are registered.
        /// Duplicate handler registrations for the same request or notification type will overwrite previous entries
        /// for IRequestHandler  and will be aggregated for INotificationHandler </remarks>
        /// <param name="assemblies">The collection of assemblies to scan for types implementing IRequestHandler  and INotificationHandler 
        /// interfaces. Cannot be null.</param>
        public HandlerRegistry(IEnumerable<Assembly> assemblies)
        {
            foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType &&
                        iface.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                    {
                        var requestType = iface.GetGenericArguments()[0];
                        RequestHandlers[requestType] = type;
                    }

                    if (iface.IsGenericType &&
                        iface.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                    {
                        var notifType = iface.GetGenericArguments()[0];

                        if (!NotificationHandlers.TryGetValue(notifType, out var list))
                        {
                            list = new List<Type>();
                            NotificationHandlers[notifType] = list;
                        }

                        list.Add(type);
                    }
                }
            }
        }
    }
}