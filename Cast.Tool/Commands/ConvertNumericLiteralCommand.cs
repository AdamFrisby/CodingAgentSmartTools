using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertNumericLiteralCommand : Command<ConvertNumericLiteralCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the numeric literal is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the numeric literal starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("--to")]
        [Description("Target format: dec, hex, or bin")]
        public string? TargetFormat { get; init; }

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

            // Find the literal expression
            var literalExpression = node.AncestorsAndSelf().OfType<LiteralExpressionSyntax>().FirstOrDefault();
            if (literalExpression == null || !literalExpression.Token.IsKind(SyntaxKind.NumericLiteralToken))
            {
                AnsiConsole.WriteLine("[red]Error: No numeric literal found at the specified location[/]");
                return 1;
            }

            var originalText = literalExpression.Token.ValueText;
            var currentFormat = GetCurrentFormat(originalText);
            var targetFormat = settings.TargetFormat?.ToLowerInvariant() ?? GetNextFormat(currentFormat);

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would convert numeric literal from {currentFormat} to {targetFormat} format[/]");
                return 0;
            }

            // Parse the value
            if (!TryParseNumericLiteral(originalText, out var value))
            {
                AnsiConsole.WriteLine("[red]Error: Could not parse numeric literal[/]");
                return 1;
            }

            // Convert to target format
            var newText = ConvertToFormat(value, targetFormat);
            if (newText == null)
            {
                AnsiConsole.WriteLine($"[red]Error: Could not convert to {targetFormat} format[/]");
                return 1;
            }

            var newToken = SyntaxFactory.Literal(newText, value);
            var newLiteralExpression = literalExpression.WithToken(newToken);

            var newRoot = root.ReplaceNode(literalExpression, newLiteralExpression);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted numeric literal from {currentFormat} to {targetFormat} format in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static string GetCurrentFormat(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return "hex";
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            return "bin";
        return "dec";
    }

    private static string GetNextFormat(string currentFormat)
    {
        return currentFormat switch
        {
            "dec" => "hex",
            "hex" => "bin",
            "bin" => "dec",
            _ => "hex"
        };
    }

    private static bool TryParseNumericLiteral(string text, out long value)
    {
        value = 0;
        
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out value);
        }
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                value = Convert.ToInt64(text.Substring(2), 2);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        return long.TryParse(text, out value);
    }

    private static string? ConvertToFormat(long value, string format)
    {
        return format switch
        {
            "dec" => value.ToString(),
            "hex" => $"0x{value:X}",
            "bin" => $"0b{Convert.ToString(value, 2)}",
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

        if (settings.TargetFormat != null && 
            !new[] { "dec", "hex", "bin" }.Contains(settings.TargetFormat.ToLowerInvariant()))
        {
            throw new ArgumentException("Target format must be dec, hex, or bin");
        }
    }
}