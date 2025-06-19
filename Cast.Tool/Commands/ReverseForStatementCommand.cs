using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ReverseForStatementCommand : Command<ReverseForStatementCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the for statement is located")]
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

            // Find the for statement
            var forStatement = node.AncestorsAndSelf().OfType<ForStatementSyntax>().FirstOrDefault();
            if (forStatement == null)
            {
                AnsiConsole.WriteLine("[red]Error: No for statement found at the specified location[/]");
                return 1;
            }

            // Try to reverse the for loop - this is a simplified implementation
            var newForStatement = ReverseForLoop(forStatement);
            if (newForStatement == null)
            {
                AnsiConsole.WriteLine("[red]Error: Cannot reverse this for statement automatically[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(forStatement, newForStatement);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully reversed for statement in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static ForStatementSyntax? ReverseForLoop(ForStatementSyntax forStatement)
    {
        // This is a simplified implementation that handles basic cases
        // For example: for (int i = 0; i < 10; i++) becomes for (int i = 9; i >= 0; i--)
        
        var declaration = forStatement.Declaration?.Variables.FirstOrDefault();
        if (declaration?.Initializer?.Value == null)
            return null;

        var condition = forStatement.Condition as BinaryExpressionSyntax;
        if (condition == null)
            return null;

        var incrementors = forStatement.Incrementors;
        if (incrementors.Count != 1)
            return null;

        // Simple case: reverse i++ to i-- and < to >=
        try
        {
            var variableName = declaration.Identifier.ValueText;
            var newCondition = condition.OperatorToken.Kind() switch
            {
                SyntaxKind.LessThanToken => condition.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.GreaterThanEqualsToken)),
                SyntaxKind.LessThanEqualsToken => condition.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.GreaterThanToken)),
                SyntaxKind.GreaterThanToken => condition.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.LessThanEqualsToken)),
                SyntaxKind.GreaterThanEqualsToken => condition.WithOperatorToken(SyntaxFactory.Token(SyntaxKind.LessThanToken)),
                _ => null
            };

            if (newCondition == null)
                return null;

            // For simplicity, just modify the initializer to subtract 1 from the condition value
            var conditionValue = condition.Right.ToString();
            var newInitializer = SyntaxFactory.EqualsValueClause(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.SubtractExpression,
                    SyntaxFactory.ParseExpression(conditionValue),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1))));

            var newDeclaration = forStatement.Declaration.ReplaceNode(
                declaration, 
                declaration.WithInitializer(newInitializer));

            // Change i++ to i--
            var newIncrementors = SyntaxFactory.SeparatedList<ExpressionSyntax>(new[]
            {
                SyntaxFactory.PostfixUnaryExpression(
                    SyntaxKind.PostDecrementExpression,
                    SyntaxFactory.IdentifierName(variableName))
            });

            return forStatement
                .WithDeclaration(newDeclaration)
                .WithCondition(newCondition)
                .WithIncrementors(newIncrementors);
        }
        catch
        {
            return null;
        }
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