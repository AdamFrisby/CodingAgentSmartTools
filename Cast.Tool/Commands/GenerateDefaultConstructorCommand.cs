using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class GenerateDefaultConstructorCommand : Command<GenerateDefaultConstructorCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the class is defined")]
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

            // Find the class or struct declaration
            var typeDeclaration = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No class or struct declaration found at the specified location[/]");
                return 1;
            }

            // Check if there's already a constructor
            var existingConstructors = typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>();
            if (existingConstructors.Any(c => c.ParameterList.Parameters.Count == 0))
            {
                AnsiConsole.WriteLine("[yellow]Default constructor already exists[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would generate default constructor for {typeDeclaration.Identifier.ValueText}[/]");
                return 0;
            }

            // Create the default constructor
            var constructorDeclaration = SyntaxFactory.ConstructorDeclaration(typeDeclaration.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                .WithParameterList(SyntaxFactory.ParameterList())
                .WithBody(SyntaxFactory.Block())
                .WithLeadingTrivia(SyntaxFactory.TriviaList(
                    SyntaxFactory.CarriageReturnLineFeed,
                    SyntaxFactory.Whitespace("        "))) // Add proper indentation
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed));

            // Add the constructor to the type
            var newTypeDeclaration = typeDeclaration.AddMembers(constructorDeclaration);
            var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully generated default constructor for {typeDeclaration.Identifier.ValueText} in {outputPath}[/]");
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