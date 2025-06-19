using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertStringLiteralCommand : Command<ConvertStringLiteralCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the string literal is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the string literal starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-t|--target")]
        [Description("Target conversion: 'verbatim' to convert to verbatim string, 'regular' to convert to regular string")]
        [DefaultValue("verbatim")]
        public string Target { get; init; } = "verbatim";

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

            // Find the string literal
            var stringLiteral = node.AncestorsAndSelf().OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(le => le.IsKind(SyntaxKind.StringLiteralExpression));

            if (stringLiteral == null)
            {
                AnsiConsole.WriteLine("[red]Error: No string literal found at the specified location[/]");
                return 1;
            }

            LiteralExpressionSyntax newStringLiteral;

            if (settings.Target.ToLower() == "verbatim")
            {
                // Convert regular string to verbatim string
                if (IsVerbatimString(stringLiteral))
                {
                    AnsiConsole.WriteLine("[yellow]String is already in verbatim format[/]");
                    return 0;
                }

                newStringLiteral = ConvertToVerbatimString(stringLiteral);
            }
            else if (settings.Target.ToLower() == "regular")
            {
                // Convert verbatim string to regular string
                if (!IsVerbatimString(stringLiteral))
                {
                    AnsiConsole.WriteLine("[yellow]String is already in regular format[/]");
                    return 0;
                }

                newStringLiteral = ConvertToRegularString(stringLiteral);
            }
            else
            {
                AnsiConsole.WriteLine("[red]Error: Target must be either 'verbatim' or 'regular'[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(stringLiteral, newStringLiteral);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted string literal to {settings.Target} format in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private bool IsVerbatimString(LiteralExpressionSyntax stringLiteral)
    {
        var token = stringLiteral.Token;
        return token.Text.StartsWith("@\"");
    }

    private LiteralExpressionSyntax ConvertToVerbatimString(LiteralExpressionSyntax stringLiteral)
    {
        var value = stringLiteral.Token.ValueText;
        
        // Escape quotes in verbatim strings (double them)
        var verbatimValue = value.Replace("\"", "\"\"");
        
        var verbatimToken = SyntaxFactory.Literal("@\"" + verbatimValue + "\"", value)
            .WithLeadingTrivia(stringLiteral.Token.LeadingTrivia)
            .WithTrailingTrivia(stringLiteral.Token.TrailingTrivia);

        return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, verbatimToken);
    }

    private LiteralExpressionSyntax ConvertToRegularString(LiteralExpressionSyntax stringLiteral)
    {
        var value = stringLiteral.Token.ValueText;
        
        // Create a regular string literal with proper escaping
        var regularToken = SyntaxFactory.Literal(value)
            .WithLeadingTrivia(stringLiteral.Token.LeadingTrivia)
            .WithTrailingTrivia(stringLiteral.Token.TrailingTrivia);

        return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, regularToken);
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

        if (settings.Target.ToLower() != "verbatim" && settings.Target.ToLower() != "regular")
        {
            throw new ArgumentException("Target must be either 'verbatim' or 'regular'");
        }
    }
}