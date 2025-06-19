using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class GenerateParameterCommand : Command<GenerateParameterCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the method call is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the undefined argument is located")]
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

            // Find the argument in a method call
            var argument = node.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            if (argument == null)
            {
                AnsiConsole.WriteLine("[red]Error: No argument found at the specified location[/]");
                return 1;
            }

            // Find the method invocation
            var invocation = argument.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation == null)
            {
                AnsiConsole.WriteLine("[red]Error: Argument must be part of a method call[/]");
                return 1;
            }

            // Find the method declaration being called
            var containingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (containingMethod == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not find containing method[/]");
                return 1;
            }

            // Generate parameter name and type
            var parameterName = "parameter" + (containingMethod.ParameterList.Parameters.Count + 1);
            var parameterType = "object"; // Default type

            // Create the new parameter
            var newParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                .WithType(SyntaxFactory.ParseTypeName(parameterType)
                    .WithTrailingTrivia(SyntaxFactory.Space));

            // Add the parameter to the method
            var newParameterList = containingMethod.ParameterList.AddParameters(newParameter);
            var newMethod = containingMethod.WithParameterList(newParameterList);

            var newRoot = root.ReplaceNode(containingMethod, newMethod);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully generated parameter '{parameterType} {parameterName}' for method '{containingMethod.Identifier.ValueText}' in {outputPath}[/]");
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