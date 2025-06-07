using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ChangeMethodSignatureCommand : Command<ChangeMethodSignatureCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the method is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the method starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-p|--parameters")]
        [Description("New parameter list in format 'type1 name1, type2 name2'")]
        public string? Parameters { get; init; }

        [CommandOption("-r|--return-type")]
        [Description("New return type for the method")]
        public string? ReturnType { get; init; }

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

            // Find the method declaration
            var method = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null)
            {
                AnsiConsole.WriteLine("[red]Error: No method declaration found at the specified location[/]");
                return 1;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would change method signature for '{method.Identifier.ValueText}' at line {settings.LineNumber}[/]");
                return 0;
            }

            var newMethod = method;

            // Change return type if specified
            if (!string.IsNullOrEmpty(settings.ReturnType))
            {
                var returnType = SyntaxFactory.ParseTypeName(settings.ReturnType)
                    .WithLeadingTrivia(method.ReturnType.GetLeadingTrivia())
                    .WithTrailingTrivia(method.ReturnType.GetTrailingTrivia());
                newMethod = newMethod.WithReturnType(returnType);
            }

            // Change parameters if specified
            if (!string.IsNullOrEmpty(settings.Parameters))
            {
                var parameters = ParseParameters(settings.Parameters);
                var parameterList = SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(parameters))
                    .WithLeadingTrivia(method.ParameterList.GetLeadingTrivia())
                    .WithTrailingTrivia(method.ParameterList.GetTrailingTrivia());
                newMethod = newMethod.WithParameterList(parameterList);
            }

            var newRoot = root.ReplaceNode(method, newMethod);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully changed method signature in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private List<ParameterSyntax> ParseParameters(string parametersText)
    {
        var parameters = new List<ParameterSyntax>();
        
        if (string.IsNullOrWhiteSpace(parametersText))
            return parameters;

        var paramParts = parametersText.Split(',');
        
        foreach (var part in paramParts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2)
            {
                var typeName = string.Join(" ", tokens.Take(tokens.Length - 1));
                var paramName = tokens.Last();

                var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                    .WithType(SyntaxFactory.ParseTypeName(typeName).WithTrailingTrivia(SyntaxFactory.Space));
                parameters.Add(parameter);
            }
        }

        return parameters;
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

        if (string.IsNullOrEmpty(settings.Parameters) && string.IsNullOrEmpty(settings.ReturnType))
        {
            throw new ArgumentException("Either parameters or return type must be specified");
        }
    }
}