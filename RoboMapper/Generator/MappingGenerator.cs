using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace RoboMapper.Generator
{
    [Generator]
    public sealed class MappingGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
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

            // Generate source output from the compilation + discovered mapping profiles.
            context.RegisterSourceOutput(compilationAndProfiles, (productionContext, pair) =>
            {
                var compilation = pair.Left;
                var profiles = pair.Right;
                Execute(productionContext, compilation, profiles);
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

        private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol> profiles)
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
            GenerateMappings(context, mappings);
            GenerateMapperConfigurationPartial(context, mappings);
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

                        // Walk chained fluent calls (ReverseMap / ForMember).
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
                                        var lambdaExpr = srcArg.Expression;
                                        if (lambdaExpr is SimpleLambdaExpressionSyntax simpleLambda)
                                            srcMember = ExtractMemberNameFromNode(simpleLambda.Body);
                                        else if (lambdaExpr is ParenthesizedLambdaExpressionSyntax parenLambda)
                                            srcMember = ExtractMemberNameFromNode(parenLambda.Body);

                                        if (!string.IsNullOrEmpty(destMember))
                                        {
                                            // Keep source member nullable to allow fallback behavior later.
                                            customMappings.Add(new PropertyMapping(destMember!, srcMember));
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
                        mappings.Add(new MappingDefinition(src, dest, reverse, customMappings));
                    }
                }
            }
        }

        private static void GenerateMappings(SourceProductionContext context, List<MappingDefinition> mappings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("using System;");
            sb.AppendLine("namespace RoboMapper");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class GeneratedMappings");
            sb.AppendLine("    {");

            // Registry of available type-to-type mappings for nested property mapping.
            var registry = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in mappings.Distinct())
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
            foreach (var m in mappings.Distinct())
            {
                EmitMappingMethod(context, sb, m.Source, m.Destination, m.CustomMappings, registry);

                if (m.Reverse)
                {
                    var revCustom = m.CustomMappings.Select(cm => new PropertyMapping(
                        DestinationName: cm.SourceName ?? cm.DestinationName,
                        SourceName: cm.DestinationName)).ToList();

                    EmitMappingMethod(context, sb, m.Destination, m.Source, revCustom, registry);
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

            var destProps = dest.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.SetMethod is not null)
                .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

            var srcProps = src.GetMembers().OfType<IPropertySymbol>()
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

        private static void GenerateMapperConfigurationPartial(SourceProductionContext context,
            List<MappingDefinition> mappings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("using System;");
            sb.AppendLine("namespace RoboMapper");
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
            sb2.AppendLine("namespace RoboMapper");
            sb2.AppendLine("{");
            sb2.AppendLine("    internal static class MapperConfiguration_Init");
            sb2.AppendLine("    {");
            sb2.AppendLine("        [ModuleInitializer]");
            sb2.AppendLine("        internal static void InitializeGenerated()");
            sb2.AppendLine("        {");

            foreach (var m in mappings.Distinct())
            {
                var srcName = m.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var destName = m.Destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var methodName = $"Map_{Sanitize(m.Source)}_{Sanitize(m.Destination)}";

                sb2.AppendLine(
                    $"            MapperConfiguration.Instance.RegisterMap(typeof({srcName}), typeof({destName}), (Func<object, {destName}>)(src => GeneratedMappings.{methodName}(({srcName})src)));");

                if (m.Reverse)
                {
                    var revMethod = $"Map_{Sanitize(m.Destination)}_{Sanitize(m.Source)}";
                    sb2.AppendLine(
                        $"            MapperConfiguration.Instance.RegisterMap(typeof({destName}), typeof({srcName}), (Func<object, {srcName}>)(src => GeneratedMappings.{revMethod}(({destName})src)));");
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

