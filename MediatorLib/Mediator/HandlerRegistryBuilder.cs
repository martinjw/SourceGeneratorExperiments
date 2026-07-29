using System.Reflection;

namespace MediatorLib.Mediator
{
    public static class HandlerRegistryBuilder
    {

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
        public static IHandlerRegistry Build(IEnumerable<Assembly> assemblies)
        {
            var handler = new HandlerRegistry();
            foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType &&
                        iface.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                    {
                        var requestType = NormalizeRequestType(iface.GetGenericArguments()[0]);
                        handler.RequestHandlers[requestType] = NormalizeHandlerType(type);
                    }

                    if (iface.IsGenericType &&
                        iface.GetGenericTypeDefinition() == typeof(IRequestHandler<>))
                    {
                        var requestType = NormalizeRequestType(iface.GetGenericArguments()[0]);
                        handler.RequestHandlers[requestType] = NormalizeHandlerType(type);
                    }

                    if (iface.IsGenericType &&
                        iface.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                    {
                        var notifType = iface.GetGenericArguments()[0];

                        if (!handler.NotificationHandlers.TryGetValue(notifType, out var list))
                        {
                            list = new List<Type>();
                            handler.NotificationHandlers[notifType] = list;
                        }

                        list.Add(type);
                    }
                }
            }

            return handler;
        }


        private static Type NormalizeRequestType(Type requestType)
        {
            if (requestType.IsGenericType && requestType.ContainsGenericParameters)
                return requestType.GetGenericTypeDefinition();

            return requestType;
        }

        private static Type NormalizeHandlerType(Type handlerType)
        {
            if (handlerType.IsGenericType && handlerType.ContainsGenericParameters)
                return handlerType.GetGenericTypeDefinition();

            return handlerType;
        }
    }
}