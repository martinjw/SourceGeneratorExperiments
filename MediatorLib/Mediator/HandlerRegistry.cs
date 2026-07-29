namespace MediatorLib.Mediator
{
    /// <summary>
    /// An empty registry that does not scan any assemblies. Handlers can be added manually to the RequestHandlers and NotificationHandlers dictionaries after instantiation.
    /// </summary>
    public class HandlerRegistry : IHandlerRegistry
    {
        public Dictionary<Type, Type> RequestHandlers { get; } = new();
        public Dictionary<Type, List<Type>> NotificationHandlers { get; } = new();

        /// <summary>
        /// Default constructor for HandlerRegistry. Initializes an empty registry without scanning any assemblies. Handlers can be added manually to the RequestHandlers and NotificationHandlers dictionaries after instantiation.
        /// </summary>
        public HandlerRegistry() { }

    }
}