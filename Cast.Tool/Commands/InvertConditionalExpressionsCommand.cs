using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class InvertConditionalExpressionsCommand : Command<InvertConditionalExpressionsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the conditional expression is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the conditional expression starts")]
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

            // Find conditional expression or binary expression
            var conditionalExpression = node.AncestorsAndSelf().OfType<ConditionalExpressionSyntax>().FirstOrDefault();
            var binaryExpression = node.AncestorsAndSelf().OfType<BinaryExpressionSyntax>().FirstOrDefault();

            if (conditionalExpression == null && binaryExpression == null)
            {
                AnsiConsole.WriteLine("[red]Error: No conditional or binary expression found at the specified location[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine("[green]Would invert conditional expression[/]");
                return 0;
            }

            SyntaxNode newRoot;
            if (conditionalExpression != null)
            {
                // Invert ternary operator: condition ? trueExpr : falseExpr becomes !condition ? falseExpr : trueExpr
                var invertedCondition = InvertExpression(conditionalExpression.Condition);
                var newConditional = SyntaxFactory.ConditionalExpression(
                    invertedCondition,
                    conditionalExpression.WhenFalse,
                    conditionalExpression.WhenTrue);

                newRoot = root.ReplaceNode(conditionalExpression, newConditional);
            }
            else if (binaryExpression != null)
            {
                // Invert binary logical expressions
                var inverted = InvertBinaryExpression(binaryExpression);
                if (inverted == null)
                {
                    AnsiConsole.WriteLine("[red]Error: Cannot invert this type of binary expression[/]");
                    return 1;
                }

                newRoot = root.ReplaceNode(binaryExpression, inverted);
            }
            else
            {
                AnsiConsole.WriteLine("[red]Error: No supported expression found[/]");
                return 1;
            }

            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully inverted conditional expression in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static ExpressionSyntax InvertExpression(ExpressionSyntax expression)
    {
        // If already negated, remove the negation
        if (expression is PrefixUnaryExpressionSyntax prefix && prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            return prefix.Operand;
        }

        // Otherwise, add negation
        return SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            SyntaxFactory.ParenthesizedExpression(expression));
    }

    private static ExpressionSyntax? InvertBinaryExpression(BinaryExpressionSyntax binary)
    {
        // Invert logical operators using De Morgan's laws
        return binary.OperatorToken.Kind() switch
        {
            SyntaxKind.AmpersandAmpersandToken => // && becomes ||
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalOrExpression,
                    InvertExpression(binary.Left),
                    InvertExpression(binary.Right)),

            SyntaxKind.BarBarToken => // || becomes &&
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    InvertExpression(binary.Left),
                    InvertExpression(binary.Right)),

            _ => null
        };
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