using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class AddDebuggerDisplayCommand : Command<AddDebuggerDisplayCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the class is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the class starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("--display-format")]
        [Description("Custom format string for the DebuggerDisplay attribute")]
        public string? DisplayFormat { get; init; }

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

            // Find the class declaration
            var classDeclaration = node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No class declaration found at the specified location[/]");
                return 1;
            }

            // Check if DebuggerDisplay attribute already exists
            var hasDebuggerDisplay = classDeclaration.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains("DebuggerDisplay"));

            if (hasDebuggerDisplay)
            {
                AnsiConsole.WriteLine("[yellow]DebuggerDisplay attribute already exists on this class[/]");
                return 0;
            }

            var displayFormat = settings.DisplayFormat ?? GenerateDefaultDisplayFormat(classDeclaration);

            // Add System.Diagnostics using if not present
            var compilationUnit = root as CompilationUnitSyntax;
            var newRoot = EnsureSystemDiagnosticsUsing(compilationUnit);

            // Find the updated class declaration
            classDeclaration = newRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .First(c => c.Identifier.ValueText == classDeclaration.Identifier.ValueText);

            // Create the DebuggerDisplay attribute
            var newClassDeclaration = AddDebuggerDisplayAttribute(classDeclaration, displayFormat);
            newRoot = newRoot.ReplaceNode(classDeclaration, newClassDeclaration);

            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = File.ReadAllText(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully added DebuggerDisplay attribute to class {classDeclaration.Identifier.ValueText}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private SyntaxNode EnsureSystemDiagnosticsUsing(CompilationUnitSyntax? compilationUnit)
    {
        if (compilationUnit == null)
            throw new InvalidOperationException("Invalid C# file structure");

        // Check if System.Diagnostics using already exists
        var hasSystemDiagnostics = compilationUnit.Usings
            .Any(u => u.Name?.ToString() == "System.Diagnostics");

        if (!hasSystemDiagnostics)
        {
            // Create the new using directive
            var newUsing = SyntaxFactory.UsingDirective(
                SyntaxFactory.IdentifierName("System.Diagnostics"))
                .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                .WithTrailingTrivia(SyntaxFactory.LineFeed);

            // Find the correct position to insert the using statement (after System)
            var usings = compilationUnit.Usings.ToList();
            var insertIndex = 0;
            for (int i = 0; i < usings.Count; i++)
            {
                var existingNamespace = usings[i].Name?.ToString() ?? "";
                if (string.Compare("System.Diagnostics", existingNamespace, StringComparison.Ordinal) > 0)
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

            compilationUnit = compilationUnit.WithUsings(SyntaxFactory.List(newUsings));
        }

        return compilationUnit;
    }

    private string GenerateDefaultDisplayFormat(ClassDeclarationSyntax classDeclaration)
    {
        var className = classDeclaration.Identifier.ValueText;
        
        // Look for common properties that would be useful in debugger display
        var properties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            .Take(3) // Limit to first 3 properties to keep display concise
            .Select(p => p.Identifier.ValueText)
            .ToList();

        if (properties.Any())
        {
            var propertyDisplays = properties.Select(p => $"{p} = {{{p}}}");
            return $"{className} {{ {string.Join(", ", propertyDisplays)} }}";
        }

        return $"{className}";
    }

    private ClassDeclarationSyntax AddDebuggerDisplayAttribute(ClassDeclarationSyntax classDeclaration, string displayFormat)
    {
        // Create the attribute argument
        var attributeArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(displayFormat)));

        // Create the attribute
        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("DebuggerDisplay"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(attributeArgument)));

        // Create the attribute list
        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute))
            .WithLeadingTrivia(classDeclaration.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        // Add the attribute to the class
        var newAttributeLists = classDeclaration.AttributeLists.Add(attributeList);
        return classDeclaration.WithAttributeLists(newAttributeLists)
            .WithLeadingTrivia(SyntaxFactory.TriviaList()); // Remove leading trivia since it's now on the attribute
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