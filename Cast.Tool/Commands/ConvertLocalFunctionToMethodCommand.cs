using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertLocalFunctionToMethodCommand : Command<ConvertLocalFunctionToMethodCommand.Settings>
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

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would convert local function '{localFunction.Identifier.ValueText}' to method[/]");
                return 0;
            }

            // Find the containing class or struct
            var containingType = localFunction.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (containingType == null)
            {
                AnsiConsole.WriteLine("[red]Error: Local function must be inside a class or struct[/]");
                return 1;
            }

            // Create the method declaration
            var methodDeclaration = SyntaxFactory.MethodDeclaration(
                localFunction.ReturnType,
                localFunction.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                .WithParameterList(localFunction.ParameterList)
                .WithBody(localFunction.Body)
                .WithTypeParameterList(localFunction.TypeParameterList)
                .WithConstraintClauses(localFunction.ConstraintClauses)
                .WithLeadingTrivia(SyntaxFactory.TriviaList(
                    SyntaxFactory.CarriageReturnLineFeed,
                    SyntaxFactory.Whitespace("        "))) // Add proper indentation
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed));

            // Remove the local function and add the method to the containing type
            var newRoot = root.RemoveNode(localFunction, SyntaxRemoveOptions.KeepNoTrivia);
            if (newRoot == null)
            {
                AnsiConsole.WriteLine("[red]Error: Failed to remove local function[/]");
                return 1;
            }

            // Find the updated containing type in the new root
            var updatedContainingType = newRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.ValueText == containingType.Identifier.ValueText);
            
            if (updatedContainingType == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not find containing type after removing local function[/]");
                return 1;
            }

            // Add the method to the type
            var newContainingType = updatedContainingType.AddMembers(methodDeclaration);
            var finalRoot = newRoot.ReplaceNode(updatedContainingType, newContainingType);
            var result = finalRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted local function '{localFunction.Identifier.ValueText}' to method in {outputPath}[/]");
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