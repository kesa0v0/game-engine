using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kesa.Engine.Analyzers
{
    /// <summary>
    /// 커널 코드에서 결정론을 깨는 구성을 막는다 (spec P2).
    /// BannedApiAnalyzers가 잡지 못하는 키워드 타입과 foreach 순회를 담당한다.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeterminismAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor FloatingPoint = new DiagnosticDescriptor(
            "KE0001",
            "Floating-point type in kernel",
            "'{0}' is not allowed in the kernel (spec P2); use int or fixed",
            "Determinism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnorderedIteration = new DiagnosticDescriptor(
            "KE0002",
            "Iteration over unordered collection",
            "Iterating '{0}' has no guaranteed order (spec P2); sort first or use an ordered collection",
            "Determinism",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(FloatingPoint, UnorderedIteration);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzePredefinedType, SyntaxKind.PredefinedType);
            context.RegisterOperationAction(AnalyzeFloatingOperation, OperationKind.Literal, OperationKind.Conversion);
            context.RegisterOperationAction(AnalyzeForEach, OperationKind.Loop);
            context.RegisterOperationAction(AnalyzeEnumerableConversion, OperationKind.Conversion);
        }

        private static void AnalyzePredefinedType(SyntaxNodeAnalysisContext ctx)
        {
            var node = (PredefinedTypeSyntax)ctx.Node;
            var kind = node.Keyword.Kind();
            if (kind == SyntaxKind.FloatKeyword || kind == SyntaxKind.DoubleKeyword || kind == SyntaxKind.DecimalKeyword)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(FloatingPoint, node.GetLocation(), node.Keyword.Text));
            }
        }

        private static void AnalyzeFloatingOperation(OperationAnalysisContext ctx)
        {
            // `var x = 1.5`, 암묵 변환처럼 키워드 없이 들어오는 경우
            if (ctx.Operation.IsImplicit && ctx.Operation.Kind == OperationKind.Conversion) return;
            var type = ctx.Operation.Type;
            if (type != null && IsFloating(type))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(FloatingPoint, ctx.Operation.Syntax.GetLocation(), type.ToDisplayString()));
            }
        }

        private static void AnalyzeForEach(OperationAnalysisContext ctx)
        {
            if (!(ctx.Operation is IForEachLoopOperation loop)) return;
            var collection = loop.Collection;
            // foreach는 컬렉션을 IEnumerable 등으로 감싼 암묵 변환을 거칠 수 있다
            while (collection is IConversionOperation conv && conv.IsImplicit) collection = conv.Operand;
            var type = collection.Type;
            if (type != null && IsUnordered(type))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(UnorderedIteration, collection.Syntax.GetLocation(), type.ToDisplayString()));
            }
        }

        private static void AnalyzeEnumerableConversion(OperationAnalysisContext ctx)
        {
            // dict.Select(...) 등 LINQ로 넘기는 경우: 순서 없는 컬렉션 → 인터페이스 변환
            var conv = (IConversionOperation)ctx.Operation;
            if (conv.Parent is IForEachLoopOperation) return;
            var source = conv.Operand.Type;
            var target = conv.Type;
            if (source == null || target == null || target.TypeKind != TypeKind.Interface) return;
            if (IsUnordered(source))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(UnorderedIteration, conv.Syntax.GetLocation(), source.ToDisplayString()));
            }
        }

        private static bool IsFloating(ITypeSymbol type) =>
            type.SpecialType == SpecialType.System_Single ||
            type.SpecialType == SpecialType.System_Double ||
            type.SpecialType == SpecialType.System_Decimal;

        private static bool IsUnordered(ITypeSymbol type)
        {
            var def = type.OriginalDefinition;
            var name = def.ContainingNamespace?.ToDisplayString() + "." + def.MetadataName;
            if (def.ContainingType != null)
            {
                name = def.ContainingType.ContainingNamespace?.ToDisplayString() + "." +
                       def.ContainingType.MetadataName + "+" + def.MetadataName;
            }

            switch (name)
            {
                case "System.Collections.Generic.Dictionary`2":
                case "System.Collections.Generic.Dictionary`2+KeyCollection":
                case "System.Collections.Generic.Dictionary`2+ValueCollection":
                case "System.Collections.Generic.HashSet`1":
                case "System.Collections.Hashtable":
                    return true;
                default:
                    return false;
            }
        }
    }
}
