using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class MakeLocalFunctionStaticCommand : Command<MakeLocalFunctionStaticCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the local function is defined")]
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

            // Find the local function declaration
            var localFunction = node.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
            if (localFunction == null)
            {
                AnsiConsole.WriteLine("[red]Error: No local function declaration found at the specified location[/]");
                return 1;
            }

            // Check if already static
            if (localFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                AnsiConsole.WriteLine($"[yellow]Local function '{localFunction.Identifier.ValueText}' is already static[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would make local function '{localFunction.Identifier.ValueText}' static[/]");
                return 0;
            }

            // Add static modifier
            var newModifiers = localFunction.Modifiers.Add(
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space));

            var newLocalFunction = localFunction.WithModifiers(newModifiers);

            var newRoot = root.ReplaceNode(localFunction, newLocalFunction);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully made local function '{localFunction.Identifier.ValueText}' static in {outputPath}[/]");
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
    }
}