using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class AddExplicitCastCommand : BaseRefactoringCommand
{
    public new class Settings : BaseRefactoringCommand.Settings
    {
        [CommandArgument(1, "<TARGET_TYPE>")]
        [Description("Target type for the explicit cast")]
        public string TargetType { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, BaseRefactoringCommand.Settings settings)
    {
        var castSettings = (Settings)settings;
        
        try
        {
            ValidateInputs(settings);
            
            if (string.IsNullOrWhiteSpace(castSettings.TargetType))
            {
                AnsiConsole.WriteLine("[red]Error: Target type is required[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);
            
            // Find an expression to cast
            var expression = node as ExpressionSyntax ?? node.Ancestors().OfType<ExpressionSyntax>().FirstOrDefault();

            if (expression == null)
            {
                AnsiConsole.WriteLine($"[yellow]Warning: No expression found at line {settings.LineNumber}, column {settings.ColumnNumber}[/]");
                return 1;
            }

            // Skip if already a cast expression
            if (expression is CastExpressionSyntax)
            {
                AnsiConsole.WriteLine("[yellow]Warning: Expression is already a cast expression[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would add explicit cast to '{castSettings.TargetType}' for expression: {expression.ToString().Trim()}[/]");
                return 0;
            }

            // Perform the cast addition
            var result = await AddExplicitCast(tree, expression, castSettings.TargetType);
            
            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);
            
            AnsiConsole.WriteLine($"[green]Successfully added explicit cast to '{castSettings.TargetType}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<string> AddExplicitCast(SyntaxTree tree, ExpressionSyntax expression, string targetType)
    {
        var root = await tree.GetRootAsync();
        
        // Parse the target type
        var typeSyntax = SyntaxFactory.ParseTypeName(targetType);
        
        // Create the cast expression
        var castExpression = SyntaxFactory.CastExpression(typeSyntax, expression)
            .WithLeadingTrivia(expression.GetLeadingTrivia())
            .WithTrailingTrivia(expression.GetTrailingTrivia());

        // Replace the original expression with the cast expression
        var newRoot = root.ReplaceNode(expression, castExpression);
        
        return newRoot.ToFullString();
    }
}