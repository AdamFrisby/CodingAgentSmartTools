using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class UseLambdaExpressionCommand : Command<UseLambdaExpressionCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the lambda expression is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the lambda expression starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-o|--output")]
        [Description("Output file path (defaults to overwriting the input file)")]
        public string? OutputPath { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show what changes would be made without applying them")]
        [DefaultValue(false)]
        public bool DryRun { get; init; } = false;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);

            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);

            // Find the lambda expression
            var lambdaExpression = node.AncestorsAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
            if (lambdaExpression == null)
            {
                AnsiConsole.WriteLine("[red]Error: No lambda expression found at the specified location[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                var currentStyle = GetLambdaStyle(lambdaExpression);
                var targetStyle = currentStyle == "expression" ? "block" : "expression";
                AnsiConsole.WriteLine($"[green]Would convert lambda from {currentStyle} to {targetStyle} body[/]");
                return 0;
            }

            // Convert between expression and block body
            var newLambda = ConvertLambdaBody(lambdaExpression);
            if (newLambda == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not convert lambda expression[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(lambdaExpression, newLambda);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted lambda expression body in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static string GetLambdaStyle(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body is BlockSyntax ? "block" : "expression",
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body is BlockSyntax ? "block" : "expression",
            _ => "unknown"
        };
    }

    private static LambdaExpressionSyntax? ConvertLambdaBody(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => ConvertSimpleLambda(simple),
            ParenthesizedLambdaExpressionSyntax parenthesized => ConvertParenthesizedLambda(parenthesized),
            _ => null
        };
    }

    private static SimpleLambdaExpressionSyntax? ConvertSimpleLambda(SimpleLambdaExpressionSyntax lambda)
    {
        if (lambda.Body is BlockSyntax block)
        {
            // Convert block to expression
            if (block.Statements.Count == 1 && block.Statements[0] is ReturnStatementSyntax returnStmt && returnStmt.Expression != null)
            {
                return lambda.WithBody(returnStmt.Expression);
            }
            else if (block.Statements.Count == 1 && block.Statements[0] is ExpressionStatementSyntax exprStmt)
            {
                return lambda.WithBody(exprStmt.Expression);
            }
        }
        else if (lambda.Body is ExpressionSyntax expression)
        {
            // Convert expression to block
            var returnStatement = SyntaxFactory.ReturnStatement(expression);
            var newBlock = SyntaxFactory.Block(returnStatement);
            return lambda.WithBody(newBlock);
        }

        return null;
    }

    private static ParenthesizedLambdaExpressionSyntax? ConvertParenthesizedLambda(ParenthesizedLambdaExpressionSyntax lambda)
    {
        if (lambda.Body is BlockSyntax block)
        {
            // Convert block to expression
            if (block.Statements.Count == 1 && block.Statements[0] is ReturnStatementSyntax returnStmt && returnStmt.Expression != null)
            {
                return lambda.WithBody(returnStmt.Expression);
            }
            else if (block.Statements.Count == 1 && block.Statements[0] is ExpressionStatementSyntax exprStmt)
            {
                return lambda.WithBody(exprStmt.Expression);
            }
        }
        else if (lambda.Body is ExpressionSyntax expression)
        {
            // Convert expression to block
            var returnStatement = SyntaxFactory.ReturnStatement(expression);
            var newBlock = SyntaxFactory.Block(returnStatement);
            return lambda.WithBody(newBlock);
        }

        return null;
    }

    private void ValidateInputs(Settings settings)
    {
        if (!File.Exists(settings.FilePath))
        {
            throw new FileNotFoundException($"File not found: {settings.FilePath}");
        }

        if (!settings.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only C# files (.cs) are supported");
        }

        if (settings.LineNumber < 1)
        {
            throw new ArgumentException("Line number must be 1 or greater");
        }

        if (settings.ColumnNumber < 0)
        {
            throw new ArgumentException("Column number must be 0 or greater");
        }
    }
}