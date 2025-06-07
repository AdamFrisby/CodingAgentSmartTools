using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class UseExplicitTypeCommand : Command<UseExplicitTypeCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the var declaration is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the var declaration starts")]
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

            // Find the variable declaration with var
            var variableDeclaration = node.AncestorsAndSelf().OfType<VariableDeclarationSyntax>().FirstOrDefault();
            if (variableDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No variable declaration found at the specified location[/]");
                return 1;
            }

            if (!variableDeclaration.Type.IsVar)
            {
                AnsiConsole.WriteLine("[yellow]Variable is already using explicit type[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would replace 'var' with explicit type at line {settings.LineNumber}[/]");
                return 0;
            }

            // Get the first variable declarator to determine the type
            var firstVariable = variableDeclaration.Variables.FirstOrDefault();
            if (firstVariable?.Initializer?.Value == null)
            {
                AnsiConsole.WriteLine("[red]Error: Cannot determine type - variable has no initializer[/]");
                return 1;
            }

            // Get the type of the initializer expression
            var typeInfo = model.GetTypeInfo(firstVariable.Initializer.Value);
            if (typeInfo.Type == null)
            {
                AnsiConsole.WriteLine("[red]Error: Cannot determine type from initializer[/]");
                return 1;
            }

            // Create the explicit type syntax
            var explicitType = SyntaxFactory.ParseTypeName(typeInfo.Type.ToDisplayString())
                .WithLeadingTrivia(variableDeclaration.Type.GetLeadingTrivia())
                .WithTrailingTrivia(variableDeclaration.Type.GetTrailingTrivia());

            var newVariableDeclaration = variableDeclaration.WithType(explicitType);
            var newRoot = root.ReplaceNode(variableDeclaration, newVariableDeclaration);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully replaced 'var' with explicit type '{typeInfo.Type.ToDisplayString()}' in {outputPath}[/]");
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