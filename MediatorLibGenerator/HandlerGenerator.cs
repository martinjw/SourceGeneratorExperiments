using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MediatorLibGenerator
{
    /// <summary>
    /// This source generator scans the referenced project code for classes that implement IRequestHandler and INotificationHandler, and generates a HandlerRegistry class that maps request and notification types to their respective handlers. This is added to the referenced project and compiled, so you have compile-time discovery of handlers without using reflection at runtime.
    /// </summary>
    // Mark this class as a Roslyn source generator
    [Generator]
    public sealed class HandlerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Create a syntax provider that finds candidate class declarations
            // The syntax provider works in two phases: a quick predicate (syntax-only) and a slower transform (semantic model)
            var candidates = context.SyntaxProvider
                .CreateSyntaxProvider(
                    // predicate: cheaply filter nodes to only class declarations that either have attributes or a base list
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds && (cds.AttributeLists.Count > 0 || cds.BaseList != null),
                    // transform: given a candidate node, get its semantic symbol (INamedTypeSymbol) via the semantic model
                    transform: static (ctx, _) =>
                    {
                        var cds = (ClassDeclarationSyntax)ctx.Node; // the class declaration syntax
                        // Get the symbol (type) declared by this class syntax; this requires the semantic model
                        var symbol = ctx.SemanticModel.GetDeclaredSymbol(cds) as INamedTypeSymbol;
                        return symbol; // may be null if symbol cannot be resolved
                    })
                // keep only non-null symbols
                .Where(static s => s is not null);

            // Collect the results into an ImmutableArray to be used by the generator output
            var allCandidates = candidates.Collect();

            // Register the source output callback; GenerateRegistry will be invoked with the collected symbols
            context.RegisterSourceOutput(allCandidates, GenerateRegistry);
        }

        // This method is invoked to produce the generated source. It receives the collected symbols.
        private static void GenerateRegistry(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> symbols)
        {
            // Accumulate textual entries for request and notification handler mappings
            var requestHandlers = new List<string>();
            var notificationHandlers = new List<string>();

            // Iterate over all discovered type symbols (skip nulls)
            foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
            {
                // Examine all interfaces implemented by the type
                foreach (var iface in symbol.AllInterfaces)
                {
                    // We're looking for IRequestHandler<TRequest, TResult>
                    // The interface symbol for a generic type has a Name and an Arity (number of type parameters)
                    if (iface.Name == "IRequestHandler" && iface.Arity == 2)
                    {
                        // Extract the request type argument (first generic parameter)
                        var requestType = iface.TypeArguments[0].ToDisplayString();
                        // Get the handler type (the current class) as a display string suitable for source emission
                        var handlerType = symbol.ToDisplayString();
                        // Add a mapping entry like: { typeof(MyRequest), typeof(MyHandler) }
                        requestHandlers.Add($"{{ typeof({requestType}), typeof({handlerType}) }}");
                    }

                    // We're also looking for INotificationHandler<TNotification>
                    if (iface.Name == "INotificationHandler" && iface.Arity == 1)
                    {
                        var notifType = iface.TypeArguments[0].ToDisplayString();
                        var handlerType = symbol.ToDisplayString();
                        // Add an entry that will later be grouped by notification type
                        notificationHandlers.Add($"{{ typeof({notifType}), typeof({handlerType}) }}");
                    }
                }
            }

            // Build the generated source file as an interpolated raw string (C# 11 raw string literal style via source generator helper)
            // The generated class exposes two dictionaries: RequestHandlers and NotificationHandlers, plus a static Builder.
            var source = $$"""
        using System;
        using System.Collections.Generic;

        namespace MediatorLib
        {
            /// <summary>
            /// This class is generated by the HandlerGenerator source generator. It contains mappings of request and notification types to their respective handlers.
            /// </summary>
            public class HandlerRegistryGenerated : IHandlerRegistry
            {
                // Map a request type to a single handler type
                public Dictionary<Type, Type> RequestHandlers =>
                    new()
                    {
                        {{string.Join(",\n                        ", requestHandlers)}}
                    };

                // Map a notification type to a list of handlers (many handlers can subscribe to the same notification type)
                public Dictionary<Type, List<Type>> NotificationHandlers =>
                    new()
                    {
                        // Group notification handler entries by the notification type key and emit an initializer for each group
                        {{string.Join(",\n                        ", notificationHandlers
                                .GroupBy(x => x.Split(',')[0])
                                .Select(g => $"{g.Key}, new List<Type>{{ {string.Join(", ", g)} }}"))}}
                    };

                /// <summary>
                /// Return the generated handler registry instance.
                /// </summary>
                public static IHandlerRegistry Build()
                {
                    return new HandlerRegistryGenerated();
                }
            }
        }
        """;

            // Add the generated source to the compilation
            context.AddSource("HandlerRegistryGenerated.g.cs", source);
        }
    }
}