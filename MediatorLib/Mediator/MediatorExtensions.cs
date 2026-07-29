using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MediatorLib.Mediator
{
    /// <summary>
    /// Provides extension methods for registering Mediator and its handlers with an IServiceCollection.
    /// </summary>
    /// <remarks>These extension methods simplify the integration of Mediator patterns into applications by
    /// enabling automatic registration of handlers via reflection or a custom handler registry. Use these methods
    /// during application startup to configure dependency injection for Mediator and its handlers.</remarks>
    public static class MediatorExtensions
    {
        /// <summary>
        /// Register Mediator and handlers using reflection based registry.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="assemblies">The assemblies to scan for handlers.</param>
        /// <returns></returns>
        public static IServiceCollection AddMediator(
            this IServiceCollection services,
            params Assembly[]? assemblies)
        {
            //use the reflection based registry
            if (assemblies != null)
            {
                return AddMediator(services, HandlerRegistryBuilder.Build(assemblies));
            }
            //let the mediator handle the registry itself (injected by DI)
            services.AddSingleton<IMediator, Mediator>();
            return services;
        }

        /// <summary>
        /// Register Mediator and handlers using a provided handler registry.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="registry">The handler registry to use.</param>
        /// <returns></returns>
        public static IServiceCollection AddMediator(this IServiceCollection services, IHandlerRegistry registry)
        {
            services.AddSingleton(registry);
            services.AddSingleton<IMediator, Mediator>();

            // Register all handlers automatically
            foreach (var handlerType in registry.RequestHandlers.Values
                         .Concat(registry.NotificationHandlers.Values.SelectMany(x => x))
                         .Distinct())
            {
                services.AddTransient(handlerType);
            }
            return services;
        }
    }
}