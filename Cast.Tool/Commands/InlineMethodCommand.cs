using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class InlineMethodCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--method-name")]
        [Description("Name of the method to inline")]
        public string MethodName { get; set; } = string.Empty;

        [CommandOption("--line")]
        [Description("Line number where the method is defined (1-based)")]
        public int LineNumber { get; set; }

        [CommandOption("--output")]
        [Description("Output file path (default: modify in place)")]
        public string? OutputPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class InlineMethodCommand : Command<InlineMethodCommandSettings>
    {
        public override int Execute(CommandContext context, InlineMethodCommandSettings settings)
        {
            try
            {
                if (!File.Exists(settings.FilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.MethodName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Method name is required[/]");
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var modifiedRoot = InlineMethod(root, settings.MethodName, settings.LineNumber);
                var modifiedCode = modifiedRoot.ToFullString();

                if (settings.DryRun)
                {
                    var originalContent = File.ReadAllText(settings.FilePath);
                    DiffUtility.DisplayDiff(originalContent, modifiedCode, settings.FilePath);
                    return 0;
                }

                var outputPath = settings.OutputPath ?? settings.FilePath;
                File.WriteAllText(outputPath, modifiedCode);

                AnsiConsole.MarkupLine("[green]Successfully inlined method '{0}' in {1}[/]", 
                    settings.MethodName, outputPath);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static CompilationUnitSyntax InlineMethod(
            CompilationUnitSyntax root, 
            string methodName, 
            int lineNumber)
        {
            // Find the method to inline
            MethodDeclarationSyntax? targetMethod = null;
            
            if (lineNumber > 0)
            {
                var sourceText = root.SyntaxTree.GetText();
                var position = sourceText.Lines[lineNumber - 1].Start;
                targetMethod = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position, 1))
                    .FirstAncestorOrSelf<MethodDeclarationSyntax>();
            }
            else
            {
                targetMethod = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.ValueText == methodName);
            }

            if (targetMethod == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' not found");
            }

            // Validate that the method can be inlined
            if (!CanInlineMethod(targetMethod))
            {
                throw new InvalidOperationException($"Method '{methodName}' cannot be inlined (too complex or has multiple return statements)");
            }

            // Find all invocations of this method
            var invocations = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => IsTargetMethodInvocation(inv, methodName))
                .ToList();

            if (!invocations.Any())
            {
                throw new InvalidOperationException($"No invocations of method '{methodName}' found");
            }

            // Replace each invocation with the method body
            var modifiedRoot = root;
            foreach (var invocation in invocations)
            {
                var inlinedExpression = CreateInlinedExpression(targetMethod, invocation);
                modifiedRoot = modifiedRoot.ReplaceNode(invocation, inlinedExpression);
            }

            // Remove the original method
            modifiedRoot = modifiedRoot.RemoveNode(targetMethod, SyntaxRemoveOptions.KeepNoTrivia)!;

            return modifiedRoot;
        }

        private static bool CanInlineMethod(MethodDeclarationSyntax method)
        {
            // Only inline simple methods with a single return statement or expression body
            if (method.ExpressionBody != null)
            {
                return true; // Expression-bodied methods are always safe to inline
            }

            if (method.Body == null)
            {
                return false; // Abstract or interface methods cannot be inlined
            }

            var statements = method.Body.Statements;
            
            // Simple case: single return statement
            if (statements.Count == 1 && statements[0] is ReturnStatementSyntax)
            {
                return true;
            }

            // Don't inline methods with multiple statements, loops, conditionals, etc.
            if (statements.Count > 1)
            {
                return false;
            }

            // Don't inline methods with complex control flow
            var hasComplexFlow = method.DescendantNodes().Any(n => 
                n is IfStatementSyntax ||
                n is WhileStatementSyntax ||
                n is ForStatementSyntax ||
                n is ForEachStatementSyntax ||
                n is SwitchStatementSyntax ||
                n is TryStatementSyntax);

            return !hasComplexFlow;
        }

        private static bool IsTargetMethodInvocation(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText == methodName,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == methodName,
                _ => false
            };
        }

        private static ExpressionSyntax CreateInlinedExpression(
            MethodDeclarationSyntax method, 
            InvocationExpressionSyntax invocation)
        {
            // Handle expression-bodied methods
            if (method.ExpressionBody != null)
            {
                return SubstituteParameters(method.ExpressionBody.Expression, method.ParameterList, invocation.ArgumentList);
            }

            // Handle methods with single return statement
            if (method.Body?.Statements.Count == 1 && 
                method.Body.Statements[0] is ReturnStatementSyntax returnStatement &&
                returnStatement.Expression != null)
            {
                return SubstituteParameters(returnStatement.Expression, method.ParameterList, invocation.ArgumentList);
            }

            // For void methods with single expression statement
            if (method.Body?.Statements.Count == 1 && 
                method.Body.Statements[0] is ExpressionStatementSyntax exprStatement)
            {
                return SubstituteParameters(exprStatement.Expression, method.ParameterList, invocation.ArgumentList);
            }

            // Fallback - return the invocation unchanged (shouldn't reach here if CanInlineMethod worked correctly)
            return invocation;
        }

        private static ExpressionSyntax SubstituteParameters(
            ExpressionSyntax expression, 
            ParameterListSyntax parameterList, 
            ArgumentListSyntax argumentList)
        {
            // Create a simple parameter substitution map
            var substitutions = new Dictionary<string, ExpressionSyntax>();
            
            var parameters = parameterList.Parameters.ToArray();
            var arguments = argumentList.Arguments.ToArray();
            
            for (int i = 0; i < Math.Min(parameters.Length, arguments.Length); i++)
            {
                substitutions[parameters[i].Identifier.ValueText] = arguments[i].Expression;
            }

            // Replace parameter references with argument expressions
            return (ExpressionSyntax)new ParameterSubstitutor(substitutions).Visit(expression);
        }

        private class ParameterSubstitutor : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, ExpressionSyntax> _substitutions;

            public ParameterSubstitutor(Dictionary<string, ExpressionSyntax> substitutions)
            {
                _substitutions = substitutions;
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_substitutions.TryGetValue(node.Identifier.ValueText, out var replacement))
                {
                    return replacement;
                }
                return base.VisitIdentifierName(node);
            }
        }
    }
}