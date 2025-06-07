using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class GenerateComparisonOperatorsCommand : Command<GenerateComparisonOperatorsCommand.Settings>
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

            // Find the class declaration
            var classDeclaration = node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No class declaration found at the specified location[/]");
                return 1;
            }

            var className = classDeclaration.Identifier.ValueText;

            // Check if comparison operators already exist
            var existingOperators = classDeclaration.Members.OfType<OperatorDeclarationSyntax>()
                .Where(op => IsComparisonOperator(op.OperatorToken.Kind()));

            if (existingOperators.Any())
            {
                AnsiConsole.WriteLine($"[yellow]Class '{className}' already has comparison operators[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would generate comparison operators for class '{className}'[/]");
                return 0;
            }

            // Generate comparison operators
            var operators = GenerateComparisonOperators(className);
            var newClass = classDeclaration.AddMembers(operators.ToArray());

            var newRoot = root.ReplaceNode(classDeclaration, newClass);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully generated comparison operators for class '{className}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static bool IsComparisonOperator(SyntaxKind operatorKind)
    {
        return operatorKind is SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or 
               SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken;
    }

    private static List<OperatorDeclarationSyntax> GenerateComparisonOperators(string className)
    {
        var operators = new List<OperatorDeclarationSyntax>();

        // Generate < operator
        operators.Add(CreateComparisonOperator(className, SyntaxKind.LessThanToken, "<"));
        
        // Generate <= operator  
        operators.Add(CreateComparisonOperator(className, SyntaxKind.LessThanEqualsToken, "<="));
        
        // Generate > operator
        operators.Add(CreateComparisonOperator(className, SyntaxKind.GreaterThanToken, ">"));
        
        // Generate >= operator
        operators.Add(CreateComparisonOperator(className, SyntaxKind.GreaterThanEqualsToken, ">="));

        return operators;
    }

    private static OperatorDeclarationSyntax CreateComparisonOperator(string className, SyntaxKind operatorKind, string operatorText)
    {
        return SyntaxFactory.OperatorDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
            SyntaxFactory.Token(operatorKind))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier("left"))
                        .WithType(SyntaxFactory.IdentifierName(className)),
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier("right"))
                        .WithType(SyntaxFactory.IdentifierName(className))
                })))
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ThrowStatement(
                    SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("NotImplementedException"))
                    .WithArgumentList(SyntaxFactory.ArgumentList()))))
            .WithLeadingTrivia(SyntaxFactory.TriviaList(
                SyntaxFactory.CarriageReturnLineFeed,
                SyntaxFactory.Whitespace("        ")))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed));
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