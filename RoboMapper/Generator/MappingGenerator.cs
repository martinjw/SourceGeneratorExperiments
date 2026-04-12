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
            var profileClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax cds && cds.BaseList is not null,
                    static (ctx, _) => GetProfileCandidate(ctx))
                .Where(static c => c is not null)
                .Select(static (c, _) => c!)
                .Collect();

            var compilationAndProfiles = context.CompilationProvider.Combine(profileClasses);

            context.RegisterSourceOutput(compilationAndProfiles, (productionContext, pair) =>
            {
                var compilation = pair.Left;
                var profiles = pair.Right;
                Execute(productionContext, compilation, profiles);
            });
        }

        private static INamedTypeSymbol? GetProfileCandidate(GeneratorSyntaxContext ctx)
        {
            if (ctx.Node is not ClassDeclarationSyntax cds)
                return null;

            var symbol = ctx.SemanticModel.GetDeclaredSymbol(cds) as INamedTypeSymbol;
            if (symbol is null)
                return null;

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
            if (profiles.IsDefaultOrEmpty)
                return;

            var mappings = new List<MappingDefinition>();

            foreach (var profile in profiles.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>())
            {
                CollectMappings(context, compilation, profile, mappings);
            }

            GenerateMappings(context, mappings);
            GenerateMapperConfigurationPartial(context, mappings);
        }

        private static void CollectMappings(
            SourceProductionContext context,
            Compilation compilation,
            INamedTypeSymbol profile,
            List<MappingDefinition> mappings)
        {
            foreach (var ctor in profile.InstanceConstructors)
            {
                if (ctor.DeclaringSyntaxReferences.Length == 0)
                    continue;

                foreach (var syntaxRef in ctor.DeclaringSyntaxReferences)
                {
                    if (syntaxRef.GetSyntax() is not ConstructorDeclarationSyntax ctorSyntax)
                        continue;

                    var body = (SyntaxNode?)ctorSyntax.Body ?? ctorSyntax.ExpressionBody;
                    if (body is null)
                        continue;

                    var invocations = body.DescendantNodes().OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        if (invocation.Expression is not SimpleNameSyntax name || name.Identifier.Text != "CreateMap")
                            continue;

                        var model = compilation.GetSemanticModel(invocation.SyntaxTree);
                        var symbolInfo = model.GetSymbolInfo(invocation);
                        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                            continue;

                        if (methodSymbol.TypeArguments.Length != 2)
                            continue;

                        var src = methodSymbol.TypeArguments[0] as INamedTypeSymbol;
                        var dest = methodSymbol.TypeArguments[1] as INamedTypeSymbol;
                        if (src is null || dest is null)
                            continue;

                        var reverse = false;
                        var customMappings = new List<PropertyMapping>();

                        SyntaxNode currentNode = invocation;
                        while (true)
                        {
                            var parent = currentNode.Parent;
                            while (parent is ParenthesizedExpressionSyntax)
                                parent = parent.Parent;

                            if (parent is MemberAccessExpressionSyntax ma && ma.Parent is InvocationExpressionSyntax chained)
                            {
                                var memberName = ma.Name.Identifier.Text;
                                if (memberName == "ReverseMap")
                                {
                                    reverse = true;
                                }
                                else if (memberName == "ForMember")
                                {
                                    if (chained.ArgumentList.Arguments.Count >= 2)
                                    {
                                        var destArg = chained.ArgumentList.Arguments[0];
                                        var srcArg = chained.ArgumentList.Arguments[1];

                                        var destName = model.GetConstantValue(destArg.Expression);
                                        string? destMember = destName.HasValue ? destName.Value?.ToString() : null;

                                        string? srcMember = null;
                                        ExpressionSyntax? lambdaExpr = srcArg.Expression as ExpressionSyntax;
                                        if (lambdaExpr is SimpleLambdaExpressionSyntax simpleLambda)
                                            srcMember = ExtractMemberNameFromNode(simpleLambda.Body);
                                        else if (lambdaExpr is ParenthesizedLambdaExpressionSyntax parenLambda)
                                            srcMember = ExtractMemberNameFromNode(parenLambda.Body);

                                        if (!string.IsNullOrEmpty(destMember))
                                        {
                                            customMappings.Add(new PropertyMapping(destMember!, srcMember));
                                        }
                                    }
                                }

                                currentNode = chained;
                                continue;
                            }

                            break;
                        }

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

            // Custom mappings first
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cm in custom)
            {
                if (!destProps.TryGetValue(cm.DestinationName, out var destProp))
                {
                    ReportDiagnosticMissingDest(context, dest, cm.DestinationName);
                    continue;
                }

                // Allow fallback to destination name when source name wasn't extracted or not found
                if (cm.SourceName is null || !srcProps.TryGetValue(cm.SourceName, out var srcProp))
                {
                    // try fallback: source property with same name as destination
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
                    // Types differ — if there's a registered mapping for the pair, emit nested map call
                    var srcTypeName = srcProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var destTypeName = destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var key = srcTypeName + "->" + destTypeName;
                    if (registry.Contains(key))
                    {
                        // Use generated mapping method for the nested types
                        if (srcProp.Type is INamedTypeSymbol srcNamed && destProp.Type is INamedTypeSymbol destNamed)
                        {
                            var mapMethod = $"Map_{Sanitize(srcNamed)}_{Sanitize(destNamed)}";
                            sb.AppendLine($"            dest.{destProp.Name} = GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
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

            // Convention-based for remaining
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
                        // If types differ and there is a registered mapping for the pair, emit a nested map call
                        var srcTypeName = srcProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var destTypeName = destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var key = srcTypeName + "->" + destTypeName;
                        if (registry.Contains(key))
                        {
                            var mapMethod = $"Map_{Sanitize((INamedTypeSymbol)srcProp.Type)}_{Sanitize((INamedTypeSymbol)destProp.Type)}";
                            sb.AppendLine($"            dest.{destProp.Name} = GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                        }
                        else
                        {
                            ReportDiagnosticTypeMismatch(context, srcProp, destProp);
                        }
                    }
                }
            }

            sb.AppendLine("            return dest;");
            sb.AppendLine("        }");
            sb.AppendLine();
            // Ensure any remaining unmatched complex properties with registered mappings are emitted
            // (covers cases where custom mappings prevented convention-based emission)
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
                    sb.AppendLine($"            dest.{destProp.Name} = GeneratedMappings.{mapMethod}(src.{srcProp.Name});");
                    emitted.Add(destProp.Name);
                }
            }
        }

        private static string Sanitize(INamedTypeSymbol type)
            => type.Name.Replace('.', '_').Replace('+', '_');

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

        private static void GenerateMapper(SourceProductionContext context)
        {
            var src = """
                      // <auto-generated />
                      using System;

                      namespace RoboMapper
                      {
                          public sealed partial class Mapper : IMapper
                          {
                              public TDestination Map<TDestination>(object source)
                              {
                                  if (source is null) throw new ArgumentNullException(nameof(source));

                                  var srcType = source.GetType();
                                  var destType = typeof(TDestination);

                                  if (!MapperConfiguration.Instance.TryGetMap(srcType, destType, out var del) || del is null)
                                      throw new InvalidOperationException($"No mapping from {srcType} to {destType}.");

                                  return ((Func<object, TDestination>)del)(source);
                              }
                          }
                      }
                      """;
            context.AddSource("Mapper.g.cs", src);
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

