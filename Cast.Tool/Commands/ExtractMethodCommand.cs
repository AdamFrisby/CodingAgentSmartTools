using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ExtractMethodCommand : BaseRefactoringCommand
{
    public new class Settings : BaseRefactoringCommand.Settings
    {
        [CommandArgument(1, "<METHOD_NAME>")]
        [Description("Name for the extracted method")]
        public string MethodName { get; init; } = string.Empty;

        [CommandOption("-e|--end-line")]
        [Description("End line number for the code selection to extract")]
        public int? EndLineNumber { get; init; }

        [CommandOption("--end-column")]
        [Description("End column number for the code selection to extract")]
        public int? EndColumnNumber { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, BaseRefactoringCommand.Settings settings)
    {
        var extractSettings = (Settings)settings;
        
        try
        {
            ValidateInputs(settings);
            
            if (string.IsNullOrWhiteSpace(extractSettings.MethodName))
            {
                AnsiConsole.WriteLine("[red]Error: Method name is required[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var startPosition = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var endPosition = extractSettings.EndLineNumber.HasValue 
                ? engine.GetTextSpanFromPosition(tree, extractSettings.EndLineNumber.Value, extractSettings.EndColumnNumber ?? 0)
                : startPosition;

            var selectionSpan = TextSpan.FromBounds(startPosition.Start, endPosition.End);
            var root = await tree.GetRootAsync();
            var selectedNodes = root.DescendantNodes()
                .Where(n => selectionSpan.Contains(n.Span))
                .ToList();

            if (!selectedNodes.Any())
            {
                AnsiConsole.WriteLine("[yellow]Warning: No code found in the specified selection[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would extract {selectedNodes.Count} nodes into method '{extractSettings.MethodName}'[/]");
                return 0;
            }

            // Perform the extraction
            var result = await PerformExtractMethod(tree, selectedNodes, extractSettings.MethodName, selectionSpan);
            
            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);
            
            AnsiConsole.WriteLine($"[green]Successfully extracted method '{extractSettings.MethodName}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<string> PerformExtractMethod(SyntaxTree tree, List<SyntaxNode> selectedNodes, string methodName, TextSpan selectionSpan)
    {
        var root = await tree.GetRootAsync();
        
        // Find the containing method or block
        var containingMethod = selectedNodes.First().Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod == null)
        {
            throw new InvalidOperationException("Selected code must be within a method");
        }

        // Find the statements to extract
        var statements = selectedNodes.OfType<StatementSyntax>().ToList();
        if (!statements.Any())
        {
            // If no complete statements, try to find the containing statement
            var containingStatement = selectedNodes.First().Ancestors().OfType<StatementSyntax>().FirstOrDefault();
            if (containingStatement != null)
            {
                statements.Add(containingStatement);
            }
            else
            {
                throw new InvalidOperationException("No statements found to extract");
            }
        }

        // Create the new method
        var extractedMethodBody = SyntaxFactory.Block(statements);
        var newMethod = SyntaxFactory.MethodDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
            methodName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
            .WithBody(extractedMethodBody);

        // Create a method call to replace the extracted code
        var methodCall = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(methodName)));

        // Replace the extracted statements with the method call
        var rewriter = new ExtractMethodRewriter(statements, methodCall);
        var newRoot = rewriter.Visit(root);

        // Add the new method to the containing class
        var containingClass = containingMethod.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (containingClass != null)
        {
            var newClass = containingClass.AddMembers(newMethod);
            newRoot = newRoot.ReplaceNode(containingClass, newClass);
        }

        return newRoot.ToFullString();
    }

    private class ExtractMethodRewriter : CSharpSyntaxRewriter
    {
        private readonly List<StatementSyntax> _statementsToReplace;
        private readonly StatementSyntax _replacementStatement;
        private bool _hasReplaced = false;

        public ExtractMethodRewriter(List<StatementSyntax> statementsToReplace, StatementSyntax replacementStatement)
        {
            _statementsToReplace = statementsToReplace;
            _replacementStatement = replacementStatement;
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            if (_hasReplaced)
                return base.VisitBlock(node);

            var statements = node.Statements.ToList();
            var firstIndex = -1;
            var lastIndex = -1;

            // Find the range of statements to replace
            for (int i = 0; i < statements.Count; i++)
            {
                if (_statementsToReplace.Contains(statements[i]))
                {
                    if (firstIndex == -1)
                        firstIndex = i;
                    lastIndex = i;
                }
            }

            if (firstIndex != -1)
            {
                // Remove the original statements and insert the method call
                var newStatements = statements.Take(firstIndex)
                    .Concat(new[] { _replacementStatement })
                    .Concat(statements.Skip(lastIndex + 1))
                    .ToArray();

                _hasReplaced = true;
                return node.WithStatements(SyntaxFactory.List(newStatements));
            }

            return base.VisitBlock(node);
        }
    }
}