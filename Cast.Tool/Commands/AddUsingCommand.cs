using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class AddUsingCommand : Command<AddUsingCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandArgument(1, "<NAMESPACE>")]
        [Description("Namespace to add as a using statement")]
        public string Namespace { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the refactoring should be applied")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the refactoring should be applied")]
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
        var usingSettings = settings;
        
        try
        {
            ValidateInputs(settings);
            
            if (string.IsNullOrWhiteSpace(usingSettings.Namespace))
            {
                AnsiConsole.WriteLine("[red]Error: Namespace is required[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would add 'using {usingSettings.Namespace};' to {settings.FilePath}[/]");
                return 0;
            }

            // Perform the using addition
            var result = await AddUsingStatement(tree, usingSettings.Namespace);
            
            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);
            
            AnsiConsole.WriteLine($"[green]Successfully added 'using {usingSettings.Namespace};' to {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<string> AddUsingStatement(SyntaxTree tree, string namespaceName)
    {
        var root = await tree.GetRootAsync();
        var compilationUnit = root as CompilationUnitSyntax;
        
        if (compilationUnit == null)
        {
            throw new InvalidOperationException("Invalid C# file structure");
        }

        // Check if the using statement already exists
        var existingUsing = compilationUnit.Usings
            .FirstOrDefault(u => u.Name?.ToString() == namespaceName);

        if (existingUsing != null)
        {
            AnsiConsole.WriteLine($"[yellow]Using statement for '{namespaceName}' already exists[/]");
            return root.ToFullString();
        }

        // Create the new using directive
        var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.IdentifierName(namespaceName))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        // Find the correct position to insert the using statement
        var usings = compilationUnit.Usings.ToList();
        
        // Insert in alphabetical order
        var insertIndex = 0;
        for (int i = 0; i < usings.Count; i++)
        {
            var existingNamespace = usings[i].Name?.ToString() ?? "";
            if (string.Compare(namespaceName, existingNamespace, StringComparison.Ordinal) > 0)
            {
                insertIndex = i + 1;
            }
            else
            {
                break;
            }
        }

        var newUsings = usings.ToList();
        newUsings.Insert(insertIndex, newUsing);

        var newCompilationUnit = compilationUnit.WithUsings(SyntaxFactory.List(newUsings));
        
        return newCompilationUnit.ToFullString();
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