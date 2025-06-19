using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertCastToAsExpressionCommand : Command<ConvertCastToAsExpressionCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the cast/as expression is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the cast/as expression starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-t|--target")]
        [Description("Target conversion: 'cast' to convert to cast, 'as' to convert to as expression")]
        [DefaultValue("as")]
        public string Target { get; init; } = "as";

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

            ExpressionSyntax? targetExpression = null;
            ExpressionSyntax? newExpression = null;

            if (settings.Target.ToLower() == "as")
            {
                // Convert cast to as expression
                var castExpression = node.AncestorsAndSelf().OfType<CastExpressionSyntax>().FirstOrDefault();
                if (castExpression == null)
                {
                    AnsiConsole.WriteLine("[red]Error: No cast expression found at the specified location[/]");
                    return 1;
                }

                targetExpression = castExpression;
                newExpression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.AsExpression,
                    castExpression.Expression,
                    SyntaxFactory.Token(SyntaxKind.AsKeyword).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                    castExpression.Type);
            }
            else if (settings.Target.ToLower() == "cast")
            {
                // Convert as expression to cast
                var asExpression = node.AncestorsAndSelf().OfType<BinaryExpressionSyntax>()
                    .FirstOrDefault(be => be.IsKind(SyntaxKind.AsExpression));
                if (asExpression == null)
                {
                    AnsiConsole.WriteLine("[red]Error: No 'as' expression found at the specified location[/]");
                    return 1;
                }

                targetExpression = asExpression;
                newExpression = SyntaxFactory.CastExpression(
                    asExpression.Right as TypeSyntax ?? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                    asExpression.Left);
            }
            else
            {
                AnsiConsole.WriteLine("[red]Error: Target must be either 'cast' or 'as'[/]");
                return 1;
            }

            if (targetExpression == null || newExpression == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not find appropriate expression to convert[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(targetExpression, newExpression);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted to {settings.Target} expression in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
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

        if (settings.ColumnNumber < 0)
        {
            throw new ArgumentException("Column number must be 0 or greater");
        }

        if (settings.Target.ToLower() != "cast" && settings.Target.ToLower() != "as")
        {
            throw new ArgumentException("Target must be either 'cast' or 'as'");
        }
    }
}