using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertStringFormatCommand : Command<ConvertStringFormatCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the String.Format call is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the String.Format call starts")]
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

            // Find the String.Format invocation
            var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation == null || !IsStringFormatCall(invocation))
            {
                AnsiConsole.WriteLine("[red]Error: No String.Format call found at the specified location[/]");
                return 1;
            }

            // Convert to interpolated string
            var interpolatedString = ConvertToInterpolatedString(invocation);
            if (interpolatedString == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not convert String.Format to interpolated string[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(invocation, interpolatedString);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted String.Format to interpolated string in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static bool IsStringFormatCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression.ToString() == "string" && 
                   memberAccess.Name.Identifier.ValueText == "Format";
        }
        return false;
    }

    private static InterpolatedStringExpressionSyntax? ConvertToInterpolatedString(InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 1)
            return null;

        // Get the format string
        var formatArg = arguments[0];
        if (formatArg.Expression is not LiteralExpressionSyntax formatLiteral ||
            !formatLiteral.Token.IsKind(SyntaxKind.StringLiteralToken))
            return null;

        var formatString = formatLiteral.Token.ValueText;
        var argumentExpressions = arguments.Skip(1).Select(arg => arg.Expression).ToList();

        // Simple conversion for basic placeholders like {0}, {1}, etc.
        var interpolationTexts = new List<InterpolatedStringContentSyntax>();

        var placeholderPattern = @"\{(\d+)(?::([^}]*))?\}";
        var matches = Regex.Matches(formatString, placeholderPattern);
        
        var lastIndex = 0;
        foreach (Match match in matches)
        {
            // Add text before the placeholder
            if (match.Index > lastIndex)
            {
                var textBefore = formatString.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrEmpty(textBefore))
                {
                    interpolationTexts.Add(SyntaxFactory.InterpolatedStringText(
                        SyntaxFactory.Token(SyntaxTriviaList.Empty, SyntaxKind.InterpolatedStringTextToken, 
                            textBefore, textBefore, SyntaxTriviaList.Empty)));
                }
            }

            // Add the interpolation
            var paramIndex = int.Parse(match.Groups[1].Value);
            if (paramIndex < argumentExpressions.Count)
            {
                var interpolation = SyntaxFactory.Interpolation(argumentExpressions[paramIndex]);
                interpolationTexts.Add(interpolation);
            }

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < formatString.Length)
        {
            var remainingText = formatString.Substring(lastIndex);
            if (!string.IsNullOrEmpty(remainingText))
            {
                interpolationTexts.Add(SyntaxFactory.InterpolatedStringText(
                    SyntaxFactory.Token(SyntaxTriviaList.Empty, SyntaxKind.InterpolatedStringTextToken, 
                        remainingText, remainingText, SyntaxTriviaList.Empty)));
            }
        }

        return SyntaxFactory.InterpolatedStringExpression(
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringStartToken),
            SyntaxFactory.List(interpolationTexts),
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringEndToken));
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