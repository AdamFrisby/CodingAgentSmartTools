using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class AddNamedArgumentCommand : Command<AddNamedArgumentCommand.Settings>
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
        [Description("Column number (0-based) where the method call starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("--parameter-index")]
        [Description("Index of the parameter to add named argument for (0-based, default: all)")]
        public int? ParameterIndex { get; init; }

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

            // Find the method invocation
            var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation == null)
            {
                AnsiConsole.WriteLine("[red]Error: No method invocation found at the specified location[/]");
                return 1;
            }

            // Get method symbol information
            var symbolInfo = model.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                AnsiConsole.WriteLine("[red]Error: Could not resolve method symbol[/]");
                return 1;
            }

            var argumentList = invocation.ArgumentList;
            if (argumentList == null || !argumentList.Arguments.Any())
            {
                AnsiConsole.WriteLine("[yellow]Method call has no arguments to add names to[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                var preview = PreviewChanges(argumentList, methodSymbol, settings.ParameterIndex);
                AnsiConsole.WriteLine($"[green]Would add named arguments to method call:[/]");
                AnsiConsole.WriteLine($"[dim]{preview}[/]");
                return 0;
            }

            // Add named arguments
            var newInvocation = AddNamedArgumentsToInvocation(invocation, methodSymbol, settings.ParameterIndex);
            var newRoot = root.ReplaceNode(invocation, newInvocation);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully added named arguments to method call in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private string PreviewChanges(ArgumentListSyntax argumentList, IMethodSymbol methodSymbol, int? parameterIndex)
    {
        var newArguments = new List<string>();
        var parameters = methodSymbol.Parameters;

        for (int i = 0; i < argumentList.Arguments.Count && i < parameters.Length; i++)
        {
            var argument = argumentList.Arguments[i];
            var parameter = parameters[i];

            if (parameterIndex.HasValue && i != parameterIndex.Value)
            {
                newArguments.Add(argument.ToString());
            }
            else if (argument.NameColon == null)
            {
                newArguments.Add($"{parameter.Name}: {argument.Expression}");
            }
            else
            {
                newArguments.Add(argument.ToString());
            }
        }

        return $"({string.Join(", ", newArguments)})";
    }

    private InvocationExpressionSyntax AddNamedArgumentsToInvocation(InvocationExpressionSyntax invocation, 
        IMethodSymbol methodSymbol, int? parameterIndex)
    {
        var argumentList = invocation.ArgumentList;
        if (argumentList == null) return invocation;

        var newArguments = new List<ArgumentSyntax>();
        var parameters = methodSymbol.Parameters;

        for (int i = 0; i < argumentList.Arguments.Count; i++)
        {
            var argument = argumentList.Arguments[i];
            
            // If we're targeting a specific parameter and this isn't it, keep as-is
            if (parameterIndex.HasValue && i != parameterIndex.Value)
            {
                newArguments.Add(argument);
                continue;
            }

            // If argument already has a name, keep as-is
            if (argument.NameColon != null)
            {
                newArguments.Add(argument);
                continue;
            }

            // If we have parameter information, add the name
            if (i < parameters.Length)
            {
                var parameter = parameters[i];
                var nameColon = SyntaxFactory.NameColon(
                    SyntaxFactory.IdentifierName(parameter.Name))
                    .WithColonToken(SyntaxFactory.Token(SyntaxKind.ColonToken)
                        .WithTrailingTrivia(SyntaxFactory.Space));

                var newArgument = argument.WithNameColon(nameColon);
                newArguments.Add(newArgument);
            }
            else
            {
                // No parameter info available, keep as-is
                newArguments.Add(argument);
            }
        }

        var newArgumentList = argumentList.WithArguments(SyntaxFactory.SeparatedList(newArguments));
        return invocation.WithArgumentList(newArgumentList);
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

        if (settings.ParameterIndex.HasValue && settings.ParameterIndex.Value < 0)
        {
            throw new ArgumentException("Parameter index must be 0 or greater");
        }
    }
}