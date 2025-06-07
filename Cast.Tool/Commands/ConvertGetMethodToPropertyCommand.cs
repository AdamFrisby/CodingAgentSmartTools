using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertGetMethodToPropertyCommand : Command<ConvertGetMethodToPropertyCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the method/property is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the method/property starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-t|--target")]
        [Description("Target conversion: 'property' to convert method to property, 'method' to convert property to method")]
        [DefaultValue("property")]
        public string Target { get; init; } = "property";

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

            if (settings.Target.ToLower() == "property")
            {
                // Convert Get method to property
                var method = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (method == null)
                {
                    AnsiConsole.WriteLine("[red]Error: No method declaration found at the specified location[/]");
                    return 1;
                }

                if (!IsGetMethod(method))
                {
                    AnsiConsole.WriteLine("[red]Error: Method does not appear to be a getter method (should start with 'Get' and return a value)[/]");
                    return 1;
                }

                if (settings.DryRun)
                {
                    AnsiConsole.WriteLine($"[green]Would convert Get method '{method.Identifier.ValueText}' to property at line {settings.LineNumber}[/]");
                    return 0;
                }

                var property = ConvertMethodToProperty(method);
                var newRoot = root.ReplaceNode(method, property);
                var result = newRoot.ToFullString();

                var outputPath = settings.OutputPath ?? settings.FilePath;
                await File.WriteAllTextAsync(outputPath, result);

                AnsiConsole.WriteLine($"[green]Successfully converted Get method to property in {outputPath}[/]");
                return 0;
            }
            else if (settings.Target.ToLower() == "method")
            {
                // Convert property to Get method
                var property = node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
                if (property == null)
                {
                    AnsiConsole.WriteLine("[red]Error: No property declaration found at the specified location[/]");
                    return 1;
                }

                if (settings.DryRun)
                {
                    AnsiConsole.WriteLine($"[green]Would convert property '{property.Identifier.ValueText}' to Get method at line {settings.LineNumber}[/]");
                    return 0;
                }

                var method = ConvertPropertyToMethod(property);
                var newRoot = root.ReplaceNode(property, method);
                var result = newRoot.ToFullString();

                var outputPath = settings.OutputPath ?? settings.FilePath;
                await File.WriteAllTextAsync(outputPath, result);

                AnsiConsole.WriteLine($"[green]Successfully converted property to Get method in {outputPath}[/]");
                return 0;
            }
            else
            {
                AnsiConsole.WriteLine("[red]Error: Target must be either 'property' or 'method'[/]");
                return 1;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private bool IsGetMethod(MethodDeclarationSyntax method)
    {
        // Check if method starts with "Get" and returns a value
        var methodName = method.Identifier.ValueText;
        return methodName.StartsWith("Get", StringComparison.OrdinalIgnoreCase) &&
               !method.ReturnType.IsKind(SyntaxKind.VoidKeyword) &&
               method.ParameterList.Parameters.Count == 0;
    }

    private PropertyDeclarationSyntax ConvertMethodToProperty(MethodDeclarationSyntax method)
    {
        var propertyName = method.Identifier.ValueText;
        if (propertyName.StartsWith("Get", StringComparison.OrdinalIgnoreCase))
        {
            propertyName = propertyName.Substring(3); // Remove "Get" prefix
        }

        var accessorList = SyntaxFactory.AccessorList(
            SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithBody(method.Body)
                    .WithExpressionBody(method.ExpressionBody)
                    .WithSemicolonToken(method.SemicolonToken)));

        var property = SyntaxFactory.PropertyDeclaration(method.ReturnType, propertyName)
            .WithModifiers(method.Modifiers)
            .WithAccessorList(accessorList)
            .WithLeadingTrivia(method.GetLeadingTrivia())
            .WithTrailingTrivia(method.GetTrailingTrivia());

        return property;
    }

    private MethodDeclarationSyntax ConvertPropertyToMethod(PropertyDeclarationSyntax property)
    {
        var methodName = "Get" + property.Identifier.ValueText;

        // Find the getter accessor
        var getter = property.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

        BlockSyntax? body = null;
        ArrowExpressionClauseSyntax? expressionBody = null;
        SyntaxToken semicolonToken = default;

        if (getter != null)
        {
            body = getter.Body;
            expressionBody = getter.ExpressionBody;
            semicolonToken = getter.SemicolonToken;
        }
        else
        {
            // Create a simple return statement for auto-properties
            var returnStatement = SyntaxFactory.ReturnStatement(
                SyntaxFactory.ThrowExpression(
                    SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("NotImplementedException"))
                    .WithArgumentList(SyntaxFactory.ArgumentList())));
            
            body = SyntaxFactory.Block(returnStatement);
        }

        var method = SyntaxFactory.MethodDeclaration(property.Type, methodName)
            .WithModifiers(property.Modifiers)
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(body)
            .WithExpressionBody(expressionBody)
            .WithSemicolonToken(semicolonToken)
            .WithLeadingTrivia(property.GetLeadingTrivia())
            .WithTrailingTrivia(property.GetTrailingTrivia());

        return method;
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

        if (settings.Target.ToLower() != "property" && settings.Target.ToLower() != "method")
        {
            throw new ArgumentException("Target must be either 'property' or 'method'");
        }
    }
}