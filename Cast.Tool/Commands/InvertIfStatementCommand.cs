using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class InvertIfStatementCommand : Command<InvertIfStatementCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the if statement is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

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

            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, 0);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);

            // Find the if statement
            var ifStatement = node.AncestorsAndSelf().OfType<IfStatementSyntax>().FirstOrDefault();
            if (ifStatement == null)
            {
                AnsiConsole.WriteLine("[red]Error: No if statement found at the specified location[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine("[green]Would invert if statement condition[/]");
                return 0;
            }

            // Invert the condition
            var invertedCondition = InvertCondition(ifStatement.Condition);
            
            // Swap the if and else bodies
            StatementSyntax newStatement;
            if (ifStatement.Else != null)
            {
                // If there's an else clause, swap the bodies
                newStatement = SyntaxFactory.IfStatement(
                    invertedCondition,
                    ifStatement.Else.Statement)
                .WithElse(SyntaxFactory.ElseClause(ifStatement.Statement));
            }
            else
            {
                // If there's no else clause, just invert the condition
                newStatement = SyntaxFactory.IfStatement(
                    invertedCondition,
                    ifStatement.Statement);
            }

            var newRoot = root.ReplaceNode(ifStatement, newStatement);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully inverted if statement in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static ExpressionSyntax InvertCondition(ExpressionSyntax condition)
    {
        // Handle simple cases
        if (condition is PrefixUnaryExpressionSyntax prefix && prefix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            // Remove the ! operator
            return prefix.Operand;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            // Invert comparison operators
            var invertedOperatorKind = binary.OperatorToken.Kind() switch
            {
                SyntaxKind.EqualsEqualsToken => SyntaxKind.ExclamationEqualsToken,
                SyntaxKind.ExclamationEqualsToken => SyntaxKind.EqualsEqualsToken,
                SyntaxKind.LessThanToken => SyntaxKind.GreaterThanEqualsToken,
                SyntaxKind.LessThanEqualsToken => SyntaxKind.GreaterThanToken,
                SyntaxKind.GreaterThanToken => SyntaxKind.LessThanEqualsToken,
                SyntaxKind.GreaterThanEqualsToken => SyntaxKind.LessThanToken,
                _ => (SyntaxKind?)null
            };

            if (invertedOperatorKind.HasValue)
            {
                return binary.WithOperatorToken(SyntaxFactory.Token(invertedOperatorKind.Value));
            }
        }

        // Default: wrap in parentheses and add ! operator
        var parenthesized = condition is ParenthesizedExpressionSyntax 
            ? condition 
            : SyntaxFactory.ParenthesizedExpression(condition);

        return SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            parenthesized);
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
    }
}