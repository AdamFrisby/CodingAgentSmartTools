using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertToInterpolatedStringCommand : Command<ConvertToInterpolatedStringCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the string concatenation is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the string concatenation starts")]
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

            // Find the binary expression (string concatenation)
            var binaryExpression = node.AncestorsAndSelf().OfType<BinaryExpressionSyntax>().FirstOrDefault();
            if (binaryExpression == null || !binaryExpression.OperatorToken.IsKind(SyntaxKind.PlusToken))
            {
                AnsiConsole.WriteLine("[red]Error: No string concatenation found at the specified location[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine("[green]Would convert string concatenation to interpolated string[/]");
                return 0;
            }

            // Convert to interpolated string
            var interpolatedString = ConvertToInterpolatedString(binaryExpression);
            if (interpolatedString == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not convert string concatenation to interpolated string[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(binaryExpression, interpolatedString);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted string concatenation to interpolated string in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static InterpolatedStringExpressionSyntax? ConvertToInterpolatedString(BinaryExpressionSyntax binaryExpression)
    {
        var parts = GetConcatenationParts(binaryExpression);
        if (parts.Count == 0)
            return null;

        var interpolationTexts = new List<InterpolatedStringContentSyntax>();

        foreach (var part in parts)
        {
            if (part is LiteralExpressionSyntax literal && literal.Token.IsKind(SyntaxKind.StringLiteralToken))
            {
                // Add string literal as text
                var text = literal.Token.ValueText;
                if (!string.IsNullOrEmpty(text))
                {
                    interpolationTexts.Add(SyntaxFactory.InterpolatedStringText(
                        SyntaxFactory.Token(SyntaxTriviaList.Empty, SyntaxKind.InterpolatedStringTextToken, 
                            text, text, SyntaxTriviaList.Empty)));
                }
            }
            else
            {
                // Add expression as interpolation
                interpolationTexts.Add(SyntaxFactory.Interpolation(part));
            }
        }

        return SyntaxFactory.InterpolatedStringExpression(
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringStartToken),
            SyntaxFactory.List(interpolationTexts),
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringEndToken));
    }

    private static List<ExpressionSyntax> GetConcatenationParts(ExpressionSyntax expression)
    {
        var parts = new List<ExpressionSyntax>();

        if (expression is BinaryExpressionSyntax binary && binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
        {
            parts.AddRange(GetConcatenationParts(binary.Left));
            parts.AddRange(GetConcatenationParts(binary.Right));
        }
        else
        {
            parts.Add(expression);
        }

        return parts;
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