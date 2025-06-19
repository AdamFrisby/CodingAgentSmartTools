using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class IntroduceUsingStatementCommand : Command<IntroduceUsingStatementCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the disposable object is used")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the disposable object starts")]
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

            // Find a variable declaration that could benefit from using statement
            var variableDeclaration = node.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
            if (variableDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No variable declaration found at the specified location[/]");
                return 1;
            }

            var variable = variableDeclaration.Declaration.Variables.FirstOrDefault();
            if (variable?.Initializer?.Value == null)
            {
                AnsiConsole.WriteLine("[red]Error: Variable must have an initializer[/]");
                return 1;
            }

            var variableName = variable.Identifier.ValueText;

            // Create the using statement
            var usingStatement = SyntaxFactory.UsingStatement(
                SyntaxFactory.VariableDeclaration(variableDeclaration.Declaration.Type)
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(variable)),
                expression: null,
                statement: SyntaxFactory.Block());

            // Find the containing block
            var containingBlock = variableDeclaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (containingBlock == null)
            {
                AnsiConsole.WriteLine("[red]Error: Variable declaration must be inside a block[/]");
                return 1;
            }

            // Replace the variable declaration with the using statement
            var newBlock = containingBlock.ReplaceNode(variableDeclaration, usingStatement);
            var newRoot = root.ReplaceNode(containingBlock, newBlock);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully introduced using statement for variable '{variableName}' in {outputPath}[/]");
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
    }
}