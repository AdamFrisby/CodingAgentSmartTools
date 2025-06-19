using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class IntroduceParameterCommand : Command<IntroduceParameterCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandArgument(1, "<PARAM_NAME>")]
        [Description("Name of the parameter to introduce")]
        public string ParameterName { get; init; } = string.Empty;

        [CommandArgument(2, "<PARAM_TYPE>")]
        [Description("Type of the parameter")]
        public string ParameterType { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the method is defined")]
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

            // Find the method declaration
            var methodDeclaration = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No method declaration found at the specified location[/]");
                return 1;
            }

            // Check if parameter already exists
            var existingParam = methodDeclaration.ParameterList.Parameters
                .FirstOrDefault(p => p.Identifier.ValueText == settings.ParameterName);
            if (existingParam != null)
            {
                AnsiConsole.WriteLine($"[yellow]Parameter '{settings.ParameterName}' already exists[/]");
                return 0;
            }

            // Create the new parameter
            var newParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(settings.ParameterName))
                .WithType(SyntaxFactory.ParseTypeName(settings.ParameterType)
                    .WithTrailingTrivia(SyntaxFactory.Space));

            // Add the parameter to the method
            var newParameterList = methodDeclaration.ParameterList.AddParameters(newParameter);
            var newMethod = methodDeclaration.WithParameterList(newParameterList);

            var newRoot = root.ReplaceNode(methodDeclaration, newMethod);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully added parameter '{settings.ParameterType} {settings.ParameterName}' to method '{methodDeclaration.Identifier.ValueText}' in {outputPath}[/]");
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

        if (string.IsNullOrWhiteSpace(settings.ParameterName))
        {
            throw new ArgumentException("Parameter name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(settings.ParameterType))
        {
            throw new ArgumentException("Parameter type cannot be empty");
        }
    }
}