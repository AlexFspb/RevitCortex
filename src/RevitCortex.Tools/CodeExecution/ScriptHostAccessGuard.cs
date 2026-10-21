using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>Protect host controls from user scripts, including aliases and escaped identifiers.</summary>
internal static class ScriptHostAccessGuard
{
    public static string[] Validate(CSharpCompilation compilation, SyntaxTree tree, int prefixLines)
    {
        var model = compilation.GetSemanticModel(tree);
        var errors = new HashSet<string>();
        foreach (var node in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var info = model.GetSymbolInfo(node);
            foreach (var symbol in info.Symbol != null ? new[] { info.Symbol } : info.CandidateSymbols.AsEnumerable())
            {
                var target = symbol is IAliasSymbol alias ? alias.Target : symbol;
                var type = target as INamedTypeSymbol ?? target.ContainingType;
                if (type == null || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)) continue;
                var ns = type.ContainingNamespace.ToDisplayString();
                var name = type.OriginalDefinition.ToDisplayString();
                // Posted ribbon commands run after the script returns. They must not be
                // able to toggle host policy outside the guarded execution lifetime.
                var ribbonAccess = ns == "Autodesk.Windows" || ns.StartsWith("Autodesk.Windows.")
                    || name == "Autodesk.Revit.UI.RevitCommandId"
                    || (name == "Autodesk.Revit.UI.UIApplication" && target.Name == "PostCommand");
                if (!ribbonAccess && ns != "RevitCortex" && !ns.StartsWith("RevitCortex.")) continue;
                if (name is "RevitCortex.Tools.CodeExecution.FamilyLoadPolicy"
                    or "RevitCortex.Tools.CodeExecution.ScriptFailureHandling"
                    or "RevitCortex.Tools.CodeExecution.ScriptFailureHandling.FailureCapture"
                    or "RevitCortex.Tools.CodeExecution.ScriptFamilyEditGuard"
                    or "RevitCortex.Core.Results.ScriptPreconditionException"
                    or "RevitCortex.Core.Results.CortexResult<T>"
                    or "RevitCortex.Core.Results.CortexError"
                    or "RevitCortex.Core.Results.CortexErrorCode") continue;
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1 - prefixLines;
                var denied = ribbonAccess ? target.ToDisplayString() : name;
                errors.Add($"CORTEX_HOST_ACCESS: Line {line}: Scripts cannot access {denied}. Host settings, approval controls and posted UI commands are not script APIs. Use direct Revit model APIs or dedicated Cortex tools.");
            }
        }
        return errors.ToArray();
    }
}
