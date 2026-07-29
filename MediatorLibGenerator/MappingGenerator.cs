using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace MediatorLibGenerator
{
    [Generator]
    public sealed class MappingGenerator : IIncrementalGenerator
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
                .Select(static (names, _) => GeneratorHelpers.ResolveGeneratedNamespace(names.Left, names.Right));

            // Discover class declarations that might inherit from MappingProfile.
            var profileClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax cds && cds.BaseList is not null,
                    static (ctx, _) => GetProfileCandidate(ctx))
                .Where(static c => c is not null)
                .Select(static (c, _) => c!)
                .Collect();

            // Combine discovered profiles with the current compilation.
            var compilationAndProfiles = context.CompilationProvider.Combine(profileClasses);
            var generationInputs = compilationAndProfiles.Combine(generatedNamespace);

            // Generate source output from the compilation + discovered mapping profiles.
            context.RegisterSourceOutput(generationInputs, (productionContext, pair) =>
            {
                var compilation = pair.Left.Left;
                var profiles = pair.Left.Right;
                var generatedNamespaceValue = pair.Right;

                if (profiles.IsDefaultOrEmpty)
                    return;

                Execute(productionContext, compilation, profiles, generatedNamespaceValue);
            });
        }

        private static INamedTypeSymbol? GetProfileCandidate(GeneratorSyntaxContext ctx)
        {
            // Only class declarations are valid profile candidates.
            if (ctx.Node is not ClassDeclarationSyntax cds)
                return null;

            // Resolve the declared symbol for the candidate class.
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(cds);
            if (symbol is null)
                return null;

            // Walk inheritance chain and keep classes deriving from MappingProfile.
            var baseType = symbol.BaseType;
            while (baseType is not null)
            {
                if (baseType.Name == "MappingProfile")
                    return symbol;
                baseType = baseType.BaseType;
            }

            return null;
        }

        private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol> profiles, string generatedNamespace)
        {
            // Nothing to generate when no profiles are discovered.
            if (profiles.IsDefaultOrEmpty)
                return;

            var mappings = new List<MappingDefinition>();

            // Aggregate mappings from each unique profile type.
            foreach (var profile in profiles.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>())
            {
                CollectMappings(context, compilation, profile, mappings);
            }

            // Emit generated map methods and startup registration code.
            GenerateMappings(context, mappings, generatedNamespace);
            GenerateMapperConfigurationPartial(context, mappings, generatedNamespace);
        }

        private static void CollectMappings(
            SourceProductionContext context,
            Compilation compilation,
            INamedTypeSymbol profile,
            List<MappingDefinition> mappings)
        {
            // Parse mapping calls from profile constructors.
            foreach (var ctor in profile.InstanceConstructors)
            {
                // Skip metadata-only constructors without syntax (can happen for referenced assemblies).
                if (ctor.DeclaringSyntaxReferences.Length == 0)
                    continue;

                foreach (var syntaxRef in ctor.DeclaringSyntaxReferences)
                {
                    // We only analyze concrete constructor declarations.
                    if (syntaxRef.GetSyntax() is not ConstructorDeclarationSyntax ctorSyntax)
                        continue;

                    // Support both block-bodied and expression-bodied constructors.
                    var body = (SyntaxNode?)ctorSyntax.Body ?? ctorSyntax.ExpressionBody;
                    if (body is null)
                        continue;

                    // Collect all invocation expressions inside constructor body.
                    var invocations = body.DescendantNodes().OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        // Resolve symbol first so CreateMap detection works across different syntax shapes.
                        var model = compilation.GetSemanticModel(invocation.SyntaxTree);
                        var symbolInfo = model.GetSymbolInfo(invocation);
                        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
                            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                        if (methodSymbol is null || methodSymbol.Name != "CreateMap")
                            continue;

                        // We only support generic CreateMap<TSource, TDestination>.
                        if (methodSymbol.TypeArguments.Length != 2)
                            continue;

                        var src = methodSymbol.TypeArguments[0] as INamedTypeSymbol;
                        var dest = methodSymbol.TypeArguments[1] as INamedTypeSymbol;
                        if (src is null || dest is null)
                            continue;

                        var reverse = false;
                        var customMappings = new List<PropertyMapping>();
                        var afterMapActions = new List<string>();

                        // Walk chained fluent calls (ReverseMap / ForMember / AfterMap).
                        SyntaxNode currentNode = invocation;
                        while (true)
                        {
                            var parent = currentNode.Parent;
                            while (parent is ParenthesizedExpressionSyntax)
                                parent = parent.Parent;

                            if (parent is MemberAccessExpressionSyntax ma && ma.Parent is InvocationExpressionSyntax chained)
                            {
                                // Read the next fluent method after CreateMap(...) in the chain.
                                var memberName = ma.Name.Identifier.Text;
                                if (memberName == "ReverseMap")
                                {
                                    // Track reverse mapping generation.
                                    reverse = true;
                                }
                                else if (memberName == "ForMember")
                                {
                                    // Capture explicit destination->source member mappings.
                                    if (chained.ArgumentList.Arguments.Count >= 2)
                                    {
                                        // Convention here: first arg selects destination member, second selects source member.
                                        var destArg = chained.ArgumentList.Arguments[0];
                                        var srcArg = chained.ArgumentList.Arguments[1];

                                        string? destMember = null;

                                        // Support string-based destination names when available.
                                        var destName = model.GetConstantValue(destArg.Expression);
                                        if (destName.HasValue)
                                        {
                                            destMember = destName.Value?.ToString();
                                        }

                                        // Also support lambda-based destination selector (d => d.Property).
                                        if (string.IsNullOrEmpty(destMember))
                                        {
                                            // Also handle destination lambda selectors like d => d.Property.
                                            if (destArg.Expression is SimpleLambdaExpressionSyntax destSimpleLambda)
                                                destMember = ExtractMemberNameFromNode(destSimpleLambda.Body);
                                            else if (destArg.Expression is ParenthesizedLambdaExpressionSyntax destParenLambda)
                                                destMember = ExtractMemberNameFromNode(destParenLambda.Body);
                                        }

                                        string? srcMember = null;
                                        string? mapFromExpression = null;

                                        if (TryExtractMapFromLambda(srcArg.Expression, out var mapFromLambda))
                                        {
                                            srcMember = ExtractMemberNameFromNode((CSharpSyntaxNode)mapFromLambda.Body);
                                            mapFromExpression = ExtractMapFromExpression(mapFromLambda, model);
                                        }

                                        if (!string.IsNullOrEmpty(destMember))
                                        {
                                            // Keep source member nullable to allow fallback behavior later.
                                            customMappings.Add(new PropertyMapping(destMember!, srcMember, mapFromExpression));
                                        }
                                    }
                                }
                                else if (memberName == "AfterMap")
                                {
                                    // Capture AfterMap actions: AfterMap((src, dest) => { ... })
                                    if (chained.ArgumentList.Arguments.Count >= 1)
                                    {
                                        var actionArg = chained.ArgumentList.Arguments[0];
                                        if (actionArg.Expression is LambdaExpressionSyntax afterMapLambda)
                                        {
                                            var afterMapCode = ExtractAfterMapExpression(afterMapLambda, model);
                                            if (!string.IsNullOrWhiteSpace(afterMapCode))
                                            {
                                                afterMapActions.Add(afterMapCode);
                                            }
                                        }
                                    }
                                }

                                // Continue climbing through the fluent call chain.
                                currentNode = chained;
                                continue;
                            }

                            // Stop when we leave the fluent mapping chain.
                            break;
                        }

                        // Store the discovered mapping definition for source generation.
                        mappings.Add(new MappingDefinition(src, dest, reverse, customMappings, afterMapActions));
                    }
                }
            }
        }

        private static void GenerateMappings(SourceProductionContext context, List<MappingDefinition> mappings, string generatedNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine($"namespace {generatedNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class GeneratedMappings");
            sb.AppendLine("    {");

            // Registry of available type-to-type mappings for nested property mapping.
            var registry = new HashSet<string>(StringComparer.Ordinal);
            var comparer = new MappingDefinitionTypeComparer();
            foreach (var m in mappings.Distinct(comparer))
            {
                var key = m.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "->" + m.Destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                registry.Add(key);
                if (m.Reverse)
                {
                    var revKey = m.Destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "->" + m.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    registry.Add(revKey);
                }
            }

            // Emit one method per discovered mapping (and reverse mapping when requested).
            foreach (var m in mappings.Distinct(comparer))
            {
                EmitMappingMethod(context, sb, m.Source, m.Destination, m.CustomMappings, m.AfterMapActions, registry);

                if (m.Reverse)
                {
                    var revCustom = m.CustomMappings.Select(cm => new PropertyMapping(
                        DestinationName: cm.SourceName ?? cm.DestinationName,
                        SourceName: cm.DestinationName,
                        MapFromExpression: null)).ToList();

                    EmitMappingMethod(context, sb, m.Destination, m.Source, revCustom, new List<string>(), registry);
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("GeneratedMappings.g.cs", sb.ToString());
        }

        private static void EmitMappingMethod(
            SourceProductionContext context,
            StringBuilder sb,
            INamedTypeSymbol src,
            INamedTypeSymbol dest,
            IReadOnlyList<PropertyMapping> custom,
            IReadOnlyList<string> afterMapActions,
            HashSet<string> registry)
        {
            var srcName = src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var destName = dest.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Build deterministic method name based on source/destination types.
            var methodName = $"Map_{Sanitize(src)}_{Sanitize(dest)}";

            sb.AppendLine($"        internal static {destName} {methodName}({srcName} src)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (src is null) throw new ArgumentNullException(nameof(src));");
            sb.AppendLine($"            var dest = new {destName}();");

            var destProps = GetAllProperties(dest)
                .Where(p => p.SetMethod is not null)
                .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

            var srcProps = GetAllProperties(src)
                .Where(p => p.GetMethod is not null)
                .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

            // Apply explicit ForMember mappings first.
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cm in custom)
            {
                if (!destProps.TryGetValue(cm.DestinationName, out var destProp))
                {
                    ReportDiagnosticMissingDest(context, dest, cm.DestinationName);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(cm.MapFromExpression))
                {
                    sb.AppendLine($"            dest.{destProp.Name} = {cm.MapFromExpression};");
                    emitted.Add(destProp.Name);
                    continue;
                }

                // Fall back to same-name source member when lambda extraction fails.
                if (cm.SourceName is null || !srcProps.TryGetValue(cm.SourceName, out var srcProp))
                {
                    // Try fallback: source property with same name as destination.
                    if (!srcProps.TryGetValue(cm.DestinationName, out srcProp))
                    {
                        ReportDiagnosticMissingSource(context, src, cm.SourceName ?? "<null>");
                        continue;
                    }
                }

                if (SymbolEqualityComparer.Default.Equals(destProp.Type, srcProp.Type))
                {
                    sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name};");
                    emitted.Add(destProp.Name);
                }
                else
                {
                    // Check for collection mapping: IEnumerable<T>/IList<T> where T has a known mapping
                    if (IsCollectionType(srcProp.Type, out var srcElementType) && 
                        IsCollectionType(destProp.Type, out var destElementType) &&
                        srcElementType is not null && destElementType is not null)
                    {
                        var srcElemTypeName = srcElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var destElemTypeName = destElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var elemKey = srcElemTypeName + "->" + destElemTypeName;

                        if (registry.Contains(elemKey))
                        {
                            if (srcElementType is INamedTypeSymbol srcElemNamed && destElementType is INamedTypeSymbol destElemNamed)
                            {
                                var mapMethod = $"Map_{Sanitize(srcElemNamed)}_{Sanitize(destElemNamed)}";
                                sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name}?.Select(item => GeneratedMappings.{mapMethod}(item)).ToList();");
                                emitted.Add(destProp.Name);
                                continue;
                            }
                        }
                    }

                    // For differing property types, recurse if a generated mapping exists.
                    var srcTypeName = srcProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var destTypeName = destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var key = srcTypeName + "->" + destTypeName;
                    if (registry.Contains(key))
                    {
                        // Use generated mapping method for nested complex types.
                        if (srcProp.Type is INamedTypeSymbol srcNamed && destProp.Type is INamedTypeSymbol destNamed)
                        {
                            var mapMethod = $"Map_{Sanitize(srcNamed)}_{Sanitize(destNamed)}";
                                if (NeedsNullGuardForNestedMap(srcProp.Type))
                                {
                                    sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name} is null ? default : GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                                }
                                else
                                {
                                    sb.AppendLine($"            dest.{destProp.Name} = GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                                }
                            emitted.Add(destProp.Name);
                        }
                        else
                        {
                            ReportDiagnosticTypeMismatch(context, srcProp, destProp);
                        }
                    }
                    else
                    {
                        ReportDiagnosticTypeMismatch(context, srcProp, destProp);
                    }
                }
            }

            // Apply convention-based same-name mapping for remaining properties.
            foreach (var destProp in destProps.Values)
            {
                if (custom.Any(c => c.DestinationName == destProp.Name))
                    continue;

                if (emitted.Contains(destProp.Name))
                    continue;

                if (srcProps.TryGetValue(destProp.Name, out var srcProp))
                {
                    if (SymbolEqualityComparer.Default.Equals(destProp.Type, srcProp.Type))
                    {
                        sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name};");
                    }
                    else
                    {
                        // Check for collection mapping: IEnumerable<T>/IList<T> where T has a known mapping
                        if (IsCollectionType(srcProp.Type, out var srcElementType) && 
                            IsCollectionType(destProp.Type, out var destElementType) &&
                            srcElementType is not null && destElementType is not null)
                        {
                            var srcElemTypeName = srcElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            var destElemTypeName = destElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            var elemKey = srcElemTypeName + "->" + destElemTypeName;

                            if (registry.Contains(elemKey))
                            {
                                if (srcElementType is INamedTypeSymbol srcElemNamed && destElementType is INamedTypeSymbol destElemNamed)
                                {
                                    var mapMethod = $"Map_{Sanitize(srcElemNamed)}_{Sanitize(destElemNamed)}";
                                    sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name}?.Select(item => GeneratedMappings.{mapMethod}(item)).ToList();");
                                    emitted.Add(destProp.Name);
                                }
                                continue;
                            }
                        }

                        // If types differ, recurse only when a known mapping exists.
                        var srcTypeName = srcProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var destTypeName = destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var key = srcTypeName + "->" + destTypeName;
                        if (registry.Contains(key))
                        {
                            var mapMethod = $"Map_{Sanitize((INamedTypeSymbol)srcProp.Type)}_{Sanitize((INamedTypeSymbol)destProp.Type)}";
                            if (NeedsNullGuardForNestedMap(srcProp.Type))
                            {
                                sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name} is null ? default : GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                            }
                            else
                            {
                                sb.AppendLine($"            dest.{destProp.Name} = GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                            }
                            emitted.Add(destProp.Name);
                        }
                        else
                        {
                            ReportDiagnosticTypeMismatch(context, srcProp, destProp);
                        }
                    }
                }
            }

            // Final pass for complex properties skipped by previous passes.
            foreach (var destProp in destProps.Values)
            {
                if (emitted.Contains(destProp.Name))
                    continue;

                if (!srcProps.TryGetValue(destProp.Name, out var srcProp))
                    continue;

                if (SymbolEqualityComparer.Default.Equals(destProp.Type, srcProp.Type))
                    continue;

                // Check for collection mapping
                if (IsCollectionType(srcProp.Type, out var srcElementType) && 
                    IsCollectionType(destProp.Type, out var destElementType) &&
                    srcElementType is not null && destElementType is not null)
                {
                    var srcElemTypeName = srcElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var destElemTypeName = destElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var elemKey = srcElemTypeName + "->" + destElemTypeName;

                    if (registry.Contains(elemKey))
                    {
                        if (srcElementType is INamedTypeSymbol srcElemNamed && destElementType is INamedTypeSymbol destElemNamed)
                        {
                            var mapMethod = $"Map_{Sanitize(srcElemNamed)}_{Sanitize(destElemNamed)}";
                            sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name}?.Select(item => GeneratedMappings.{mapMethod}(item)).ToList();");
                            emitted.Add(destProp.Name);
                            continue;
                        }
                    }
                }

                var srcTypeName = srcProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var destTypeName = destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var key = srcTypeName + "->" + destTypeName;
                if (registry.Contains(key) && srcProp.Type is INamedTypeSymbol srcNamed && destProp.Type is INamedTypeSymbol destNamed)
                {
                    var mapMethod = $"Map_{Sanitize(srcNamed)}_{Sanitize(destNamed)}";
                    if (NeedsNullGuardForNestedMap(srcProp.Type))
                    {
                        sb.AppendLine($"            dest.{destProp.Name} = src.{srcProp.Name} is null ? default : GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                    }
                    else
                    {
                        sb.AppendLine($"            dest.{destProp.Name} = GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                    }
                    emitted.Add(destProp.Name);
                }
            }

            // Apply AfterMap actions
            foreach (var afterMapAction in afterMapActions)
            {
                sb.AppendLine($"            {afterMapAction}");
            }

            sb.AppendLine("            return dest;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string Sanitize(INamedTypeSymbol type)
        {
            // Build identifier-safe names for generated method names.
            var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var sb = new StringBuilder(fullName.Length);

            foreach (var ch in fullName)
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            return sb.ToString();
        }

        private static bool NeedsNullGuardForNestedMap(ITypeSymbol type)
            => type.IsReferenceType
               || (type is INamedTypeSymbol named
                   && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

        private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol type)
        {
            var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
            var currentType = type;

            // Walk up the inheritance chain
            while (currentType is not null)
            {
                foreach (var member in currentType.GetMembers())
                {
                    if (member is IPropertySymbol property && !properties.ContainsKey(property.Name))
                    {
                        // Add property if not already present (most derived wins)
                        properties[property.Name] = property;
                    }
                }

                currentType = currentType.BaseType;
            }

            return properties.Values;
        }

        private static bool IsCollectionType(ITypeSymbol type, out ITypeSymbol? elementType)
        {
            elementType = null;

            if (type is not INamedTypeSymbol namedType)
                return false;

            // Check for IEnumerable<T>, IList<T>, ICollection<T>, List<T>
            if (namedType.IsGenericType && namedType.TypeArguments.Length == 1)
            {
                var typeName = namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (typeName == "global::System.Collections.Generic.IEnumerable<T>" ||
                    typeName == "global::System.Collections.Generic.IList<T>" ||
                    typeName == "global::System.Collections.Generic.ICollection<T>" ||
                    typeName == "global::System.Collections.Generic.List<T>")
                {
                    elementType = namedType.TypeArguments[0];
                    return true;
                }
            }

            // Also check if type implements IEnumerable<T>
            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.IsGenericType && iface.TypeArguments.Length == 1)
                {
                    var ifaceName = iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (ifaceName == "global::System.Collections.Generic.IEnumerable<T>")
                    {
                        elementType = iface.TypeArguments[0];
                        return true;
                    }
                }
            }

            return false;
        }

        private static string? ExtractMemberNameFromNode(CSharpSyntaxNode node)
        {
            // Handle expression bodies
            if (node is ExpressionSyntax expr)
            {
                // unwrap conversions and parenthesis
                while (expr is ParenthesizedExpressionSyntax par)
                    expr = par.Expression;

                if (expr is MemberAccessExpressionSyntax ma)
                    return ma.Name.Identifier.Text;

                // conditional access: src?.Prop
                if (expr is ConditionalAccessExpressionSyntax ca && ca.WhenNotNull is MemberBindingExpressionSyntax mb)
                    return mb.Name.Identifier.Text;

                // casted/member access: ((SomeType)src).Prop
                if (expr is BinaryExpressionSyntax)
                    return null;

                // invocation wrappers: src.Method().Prop -> can't easily resolve here
                if (expr is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax im)
                    return im.Name.Identifier.Text;
            }

            // Handle block bodies: { return src.Prop; }
            if (node is BlockSyntax block)
            {
                var ret = block.DescendantNodes().OfType<ReturnStatementSyntax>().FirstOrDefault();
                if (ret?.Expression is ExpressionSyntax retExpr)
                    return ExtractMemberNameFromNode(retExpr);
            }

            return null;
        }

        private static bool TryExtractMapFromLambda(ExpressionSyntax expression, out LambdaExpressionSyntax mapFromLambda)
        {
            mapFromLambda = null!;

            if (expression is not LambdaExpressionSyntax lambda)
                return false;

            if (TryGetLambdaBodyExpression(lambda) is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "MapFrom"
                && invocation.ArgumentList.Arguments.Count > 0
                && invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax innerLambda)
            {
                mapFromLambda = innerLambda;
                return true;
            }

            mapFromLambda = lambda;
            return true;
        }

        private static string? ExtractMapFromExpression(LambdaExpressionSyntax lambda, SemanticModel model)
        {
            var bodyExpression = TryGetLambdaBodyExpression(lambda);
            if (bodyExpression is null)
                return null;

            // First, fully qualify type names on the ORIGINAL expression (which is part of the semantic model's tree)
            var rewritten = (ExpressionSyntax)new TypeNameQualificationRewriter(model).Visit(bodyExpression)!;

            // Then, rename lambda parameters on the qualified expression
            var lambdaParameterName = GetSingleLambdaParameterName(lambda);
            if (!string.IsNullOrEmpty(lambdaParameterName) && lambdaParameterName != "src")
            {
                rewritten = (ExpressionSyntax)new LambdaParameterRenameRewriter(lambdaParameterName!, "src").Visit(rewritten)!;
            }

            return rewritten.WithoutTrivia().ToString();
        }

        private static string? ExtractAfterMapExpression(LambdaExpressionSyntax lambda, SemanticModel model)
        {
            // AfterMap expects (src, dest) => { ... }
            // We need to extract the body and convert it to executable code
            if (lambda.Body is BlockSyntax blockSyntax)
            {
                // Fully qualify type names
                var rewritten = (BlockSyntax)new TypeNameQualificationRewriter(model).Visit(blockSyntax)!;

                // Get parameter names from the lambda
                string? srcParamName = null;
                string? destParamName = null;

                if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
                {
                    srcParamName = simpleLambda.Parameter.Identifier.Text;
                }
                else if (lambda is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
                {
                    if (parenthesizedLambda.ParameterList.Parameters.Count >= 2)
                    {
                        srcParamName = parenthesizedLambda.ParameterList.Parameters[0].Identifier.Text;
                        destParamName = parenthesizedLambda.ParameterList.Parameters[1].Identifier.Text;
                    }
                }

                // Rename parameters to match generated code (src and dest)
                if (!string.IsNullOrEmpty(srcParamName) && srcParamName != "src")
                {
                    rewritten = (BlockSyntax)new LambdaParameterRenameRewriter(srcParamName!, "src").Visit(rewritten)!;
                }
                if (!string.IsNullOrEmpty(destParamName) && destParamName != "dest")
                {
                    rewritten = (BlockSyntax)new LambdaParameterRenameRewriter(destParamName!, "dest").Visit(rewritten)!;
                }

                // Extract statements from block and format them
                var statements = rewritten.Statements;
                var codeBuilder = new StringBuilder();
                foreach (var statement in statements)
                {
                    var statementText = statement.WithoutTrivia().ToString();
                    codeBuilder.AppendLine(statementText);
                }

                return codeBuilder.ToString().TrimEnd();
            }
            else if (lambda.Body is ExpressionSyntax expressionSyntax)
            {
                // Handle single expression: (src, dest) => dest.Property = src.Value
                var rewritten = (ExpressionSyntax)new TypeNameQualificationRewriter(model).Visit(expressionSyntax)!;

                // Get parameter names from the lambda
                string? srcParamName = null;
                string? destParamName = null;

                if (lambda is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
                {
                    if (parenthesizedLambda.ParameterList.Parameters.Count >= 2)
                    {
                        srcParamName = parenthesizedLambda.ParameterList.Parameters[0].Identifier.Text;
                        destParamName = parenthesizedLambda.ParameterList.Parameters[1].Identifier.Text;
                    }
                }

                // Rename parameters to match generated code
                if (!string.IsNullOrEmpty(srcParamName) && srcParamName != "src")
                {
                    rewritten = (ExpressionSyntax)new LambdaParameterRenameRewriter(srcParamName!, "src").Visit(rewritten)!;
                }
                if (!string.IsNullOrEmpty(destParamName) && destParamName != "dest")
                {
                    rewritten = (ExpressionSyntax)new LambdaParameterRenameRewriter(destParamName!, "dest").Visit(rewritten)!;
                }

                return rewritten.WithoutTrivia().ToString() + ";";
            }

            return null;
        }

        private static string? GetSingleLambdaParameterName(LambdaExpressionSyntax lambda)
        {
            if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
                return simpleLambda.Parameter.Identifier.Text;

            if (lambda is ParenthesizedLambdaExpressionSyntax parenthesizedLambda
                && parenthesizedLambda.ParameterList.Parameters.Count == 1)
            {
                return parenthesizedLambda.ParameterList.Parameters[0].Identifier.Text;
            }

            return null;
        }

        private static ExpressionSyntax? TryGetLambdaBodyExpression(LambdaExpressionSyntax lambda)
        {
            if (lambda.Body is ExpressionSyntax expressionSyntax)
                return expressionSyntax;

            if (lambda.Body is BlockSyntax blockSyntax)
            {
                return blockSyntax.Statements
                    .OfType<ReturnStatementSyntax>()
                    .Select(statement => statement.Expression)
                    .OfType<ExpressionSyntax>()
                    .FirstOrDefault();
            }

            return null;
        }

        private sealed class LambdaParameterRenameRewriter : CSharpSyntaxRewriter
        {
            private readonly string oldName;
            private readonly string newName;

            public LambdaParameterRenameRewriter(string oldName, string newName)
            {
                this.oldName = oldName;
                this.newName = newName;
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.Identifier.Text == oldName)
                {
                    return SyntaxFactory.IdentifierName(newName).WithTriviaFrom(node);
                }

                return base.VisitIdentifierName(node);
            }
        }

        private sealed class TypeNameQualificationRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel semanticModel;

            public TypeNameQualificationRewriter(SemanticModel semanticModel)
            {
                this.semanticModel = semanticModel;
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                // Defensively check if this node is part of the semantic model's syntax tree
                // to avoid "Syntax node is not within syntax tree" exceptions
                if (node.SyntaxTree != semanticModel.SyntaxTree)
                {
                    return base.VisitIdentifierName(node);
                }

                var symbol = semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol is INamedTypeSymbol namedType)
                {
                    var qualified = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return SyntaxFactory.ParseName(qualified).WithTriviaFrom(node);
                }

                return base.VisitIdentifierName(node);
            }
        }

        private static void GenerateMapperConfigurationPartial(SourceProductionContext context,
            List<MappingDefinition> mappings,
            string generatedNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("using System;");
            sb.AppendLine($"namespace {generatedNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class MapperConfiguration_Initializer");
            sb.AppendLine("    {");
            sb.AppendLine("        // Called by module initializer in the generated Init file in the consuming project.");
            sb.AppendLine("        internal static void Register() { }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            context.AddSource("MapperConfiguration.Partial.g.cs", sb.ToString());

            // Generate a module initializer that registers all generated mappings.
            var sb2 = new StringBuilder();
            sb2.AppendLine("// <auto-generated />");
            sb2.AppendLine("using System;");
            sb2.AppendLine("using System.Runtime.CompilerServices;");
            sb2.AppendLine($"namespace {generatedNamespace}");
            sb2.AppendLine("{");
            sb2.AppendLine("    internal static class MapperConfiguration_Init");
            sb2.AppendLine("    {");
            sb2.AppendLine("        [ModuleInitializer]");
            sb2.AppendLine("        internal static void InitializeGenerated()");
            sb2.AppendLine("        {");

            var comparer = new MappingDefinitionTypeComparer();
            foreach (var m in mappings.Distinct(comparer))
            {
                var srcName = m.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var destName = m.Destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var methodName = $"Map_{Sanitize(m.Source)}_{Sanitize(m.Destination)}";
                //there is only one MapperConfiguration
                sb2.AppendLine(
                    $"            global::MediatorLib.Mapping.MapperConfiguration.Instance.RegisterMap(typeof({srcName}), typeof({destName}), (Func<object, {destName}>)(src => GeneratedMappings.{methodName}(({srcName})src)));");

                if (m.Reverse)
                {
                    var revMethod = $"Map_{Sanitize(m.Destination)}_{Sanitize(m.Source)}";
                    sb2.AppendLine(
                        $"            global::MediatorLib.Mapping.MapperConfiguration.Instance.RegisterMap(typeof({destName}), typeof({srcName}), (Func<object, {srcName}>)(src => GeneratedMappings.{revMethod}(({destName})src)));");
                }
            }

            sb2.AppendLine("        }");
            sb2.AppendLine("    }");
            sb2.AppendLine("}");

            context.AddSource("MapperConfiguration.Init.g.cs", sb2.ToString());
        }

        // Diagnostics

        private static readonly DiagnosticDescriptor MissingDestPropertyRule =
            new DiagnosticDescriptor(
                id: "MAPDESTNOTFOUND",
                title: "Destination property not found",
                messageFormat: "Destination property '{0}' not found on type '{1}'",
                category: "Mapping",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingSourcePropertyRule =
            new DiagnosticDescriptor(
                id: "MAPSRCNOTFOUND",
                title: "Source property not found",
                messageFormat: "Source property '{0}' not found on type '{1}'",
                category: "Mapping",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor TypeMismatchRule =
            new DiagnosticDescriptor(
                id: "MAPTYPEMISMATCH",
                title: "Property type mismatch",
                messageFormat: "Cannot map property '{0}' ({1}) to '{2}' ({3})",
                category: "Mapping",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

        private static void ReportDiagnosticMissingDest(SourceProductionContext context, INamedTypeSymbol dest,
            string destProp)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingDestPropertyRule,
                Location.None,
                destProp,
                dest.ToDisplayString()));
        }

        private static void ReportDiagnosticMissingSource(SourceProductionContext context, INamedTypeSymbol src,
            string srcProp)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingSourcePropertyRule,
                Location.None,
                srcProp,
                src.ToDisplayString()));
        }

        private static void ReportDiagnosticTypeMismatch(SourceProductionContext context, IPropertySymbol srcProp,
            IPropertySymbol destProp)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TypeMismatchRule,
                Location.None,
                srcProp.Name,
                srcProp.Type.ToDisplayString(),
                destProp.Name,
                destProp.Type.ToDisplayString()));
        }
    }
}

