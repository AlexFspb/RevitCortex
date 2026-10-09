using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>Semantic instrumentation of direct API calls, not a textual replacement or a full sandbox.</summary>
internal static class FamilyEditCallRewriter
{
    public const string ErrorPrefix = "CORTEX_FAMILY_GUARD: ";

    public static SyntaxTree Rewrite(CSharpCompilation compilation, SyntaxTree tree, string mode,
        int prefixLines, out string[] errors)
    {
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var problems = new List<string>();
        var calls = new List<InvocationExpressionSyntax>();
        foreach (var name in root.DescendantNodes().OfType<IdentifierNameSyntax>()
                     .Where(n => n.Identifier.ValueText == "EditFamily"))
        {
            var info = model.GetSymbolInfo(name);
            var symbol = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.ContainingType.ToDisplayString() == "Autodesk.Revit.DB.Document" ||
                    m.ContainingType.ToDisplayString() == "RevitCortex.Tools.CodeExecution.ScriptFamilyEditGuard");
            if (symbol == null) continue; // Ordinary compilation diagnoses unresolved calls.
            var owner = symbol.ContainingType.ToDisplayString();
            if (owner != "Autodesk.Revit.DB.Document" &&
                owner != "RevitCortex.Tools.CodeExecution.ScriptFamilyEditGuard") continue;
            // nameof does not invoke or capture the method.
            if (name.Ancestors().OfType<InvocationExpressionSyntax>().Any(i =>
                    i.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "nameof")) continue;

            string? problem = null;
            if (mode != "none") problem = "EditFamily requires transactionMode=none; auto/group are rejected before script execution.";
            else if (info.Symbol == null) continue; // Preserve original overload diagnostics; never rewrite unresolved calls.
            else if (owner != "Autodesk.Revit.DB.Document") continue; // Explicit guarded helper is already protected.
            else if (name.Parent is MemberAccessExpressionSyntax member &&
                     member.Parent is InvocationExpressionSyntax call && call.Expression == member)
                calls.Add(call);
            else problem = "Use a direct document.EditFamily(family) call. Method-group capture and conditional access are not supported by the Cortex guard.";

            if (problem != null)
            {
                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1 - prefixLines;
                problems.Add($"{ErrorPrefix}Line {line}: {problem}");
            }
        }
        errors = problems.ToArray();
        if (errors.Length != 0 || calls.Count == 0) return tree;
        var rewritten = root.ReplaceNodes(calls, (original, visited) =>
        {
            var member = (MemberAccessExpressionSyntax)visited.Expression;
            var arguments = new[] { SyntaxFactory.Argument(member.Expression.WithoutTrivia()) }
                .Concat(visited.ArgumentList.Arguments);
            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression("global::RevitCortex.Tools.CodeExecution.ScriptFamilyEditGuard.EditFamily"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
                .WithTriviaFrom(visited);
        });
        return tree.WithRootAndOptions(rewritten, tree.Options);
    }
}
