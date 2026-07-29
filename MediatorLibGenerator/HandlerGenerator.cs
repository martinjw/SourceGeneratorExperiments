using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            var generatedNamespace = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) =>
                {
                    provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
                    return rootNamespace;
                })
                .Combine(context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName))
                .Select(static (names, _) => ResolveGeneratedNamespace(names.Left, names.Right));

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
            context.RegisterSourceOutput(allCandidates.Combine(generatedNamespace), GenerateRegistry);
        }

        // This method is invoked to produce the generated source. It receives the collected symbols.
        private static void GenerateRegistry(SourceProductionContext context, (ImmutableArray<INamedTypeSymbol?> symbols, string generatedNamespace) input)
        {
            var symbols = input.symbols;
            var generatedNamespace = input.generatedNamespace;

            var typeDisplayFormat = new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

            // Accumulate textual entries for request and notification handler mappings
            var requestHandlers = new HashSet<(string requestType, string handlerType)>();
            var notificationHandlers = new Dictionary<string, HashSet<string>>();

            // Iterate over all discovered type symbols (skip nulls)
            foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
            {
                // Skip abstract handlers because they cannot be directly resolved.
                if (symbol.IsAbstract)
                    continue;

                // Examine all interfaces implemented by the type
                foreach (var iface in symbol.AllInterfaces)
                {
                    // We're looking for IRequestHandler<TRequest, TResult>
                    // The interface symbol for a generic type has a Name and an Arity (number of type parameters)
                    if (iface.Name == "IRequestHandler" && iface.Arity is 2 or 1)
                    {
                        var requestType = BuildTypeExpression(iface.TypeArguments[0], typeDisplayFormat);
                        if (requestType is null)
                            continue;

                        // Get the handler type (the current class) as a display string suitable for source emission
                        var handlerType = BuildTypeExpression(symbol, typeDisplayFormat);
                        if (handlerType is null)
                            continue;

                        // Add a mapping entry like: { typeof(MyRequest), typeof(MyHandler) }
                        requestHandlers.Add((requestType, handlerType));
                    }

                    // We're also looking for INotificationHandler<TNotification>
                    if (iface.Name == "INotificationHandler" && iface.Arity == 1)
                    {
                        var notifType = BuildTypeExpression(iface.TypeArguments[0], typeDisplayFormat);
                        if (notifType is null)
                            continue;

                        var handlerType = BuildTypeExpression(symbol, typeDisplayFormat);
                        if (handlerType is null)
                            continue;

                        if (!notificationHandlers.TryGetValue(notifType, out var handlers))
                        {
                            handlers = new HashSet<string>();
                            notificationHandlers[notifType] = handlers;
                        }

                        handlers.Add(handlerType);
                    }
                }
            }

            var requestHandlerLines = requestHandlers
                .OrderBy(x => x.requestType, StringComparer.Ordinal)
                .Select(x => $"{{ typeof({x.requestType}), typeof({x.handlerType}) }}")
                .ToList();

            var notificationHandlerLines = notificationHandlers
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{{ typeof({x.Key}), new List<Type>{{ {string.Join(", ", x.Value.OrderBy(h => h, StringComparer.Ordinal).Select(h => $"typeof({h})"))} }} }}")
                .ToList();

            if (requestHandlerLines.Count == 0 && notificationHandlerLines.Count == 0)
                return;

            // Build the generated source file as an interpolated raw string (C# 11 raw string literal style via source generator helper)
            // The generated class exposes two dictionaries: RequestHandlers and NotificationHandlers, plus a static Builder.
            var source = $$"""
        using System;
        using System.Collections.Generic;

        namespace {{generatedNamespace}}
        {
            /// <summary>
            /// This class is generated by the HandlerGenerator source generator. It contains mappings of request and notification types to their respective handlers.
            /// </summary>
            public class HandlerRegistryGenerated : MediatorLib.Mediator.IHandlerRegistry
            {
                // Map a request type to a single handler type
                public Dictionary<Type, Type> RequestHandlers =>
                    new()
                    {
                        {{string.Join(",\n                        ", requestHandlerLines)}}
                    };

                // Map a notification type to a list of handlers (many handlers can subscribe to the same notification type)
                public Dictionary<Type, List<Type>> NotificationHandlers =>
                    new()
                    {
                        {{string.Join(",\n                        ", notificationHandlerLines)}}
                    };

                /// <summary>
                /// Return the generated handler registry instance.
                /// </summary>
                public static MediatorLib.Mediator.IHandlerRegistry Build()
                {
                    return new HandlerRegistryGenerated();
                }
            }
        }

        //public static class HandlerRegistryGenerated
        //{
        //    public static MediatorLib.Mediator.IHandlerRegistry Build()
        //    {
        //        return global::{{generatedNamespace}}.HandlerRegistryGenerated.Build();
        //    }
        //}
        """;

            // Add the generated source to the compilation
            context.AddSource("HandlerRegistryGenerated.g.cs", source);
        }

        private static string ResolveGeneratedNamespace(string? rootNamespace, string? assemblyName)
        {
            var candidate = string.IsNullOrWhiteSpace(rootNamespace)
                ? assemblyName
                : rootNamespace;

            if (string.IsNullOrWhiteSpace(candidate))
                return "MediatorLib.Mediator";

            var normalized = string.Join(".", candidate
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static segment =>
                {
                    var sanitized = segment
                        .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_')
                        .ToArray();

                    if (sanitized.Length == 0)
                        return "_";

                    if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
                        return "_" + new string(sanitized);

                    return new string(sanitized);
                }));

            return string.IsNullOrWhiteSpace(normalized)
                ? "MediatorLib.Mediator"
                : normalized;
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
                return true;

            return type is INamedTypeSymbol namedType && namedType.TypeArguments.Any(ContainsTypeParameter);
        }

        private static string? BuildTypeExpression(ITypeSymbol type, SymbolDisplayFormat displayFormat)
        {
            if (type is not INamedTypeSymbol namedType)
                return null;

            if (ContainsTypeParameter(namedType))
            {
                if (!namedType.IsGenericType)
                    return null;

                return namedType.ConstructUnboundGenericType().ToDisplayString(displayFormat);
            }

            return namedType.ToDisplayString(displayFormat);
        }
    }
}