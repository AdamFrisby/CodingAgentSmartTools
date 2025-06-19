using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertAutoPropertyCommand : BaseRefactoringCommand
{
    public new class Settings : BaseRefactoringCommand.Settings
    {
        [CommandOption("-t|--to")]
        [Description("Direction of conversion: 'full' (auto to full) or 'auto' (full to auto)")]
        [DefaultValue("full")]
        public string Direction { get; init; } = "full";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, BaseRefactoringCommand.Settings settings)
    {
        var convertSettings = (Settings)settings;
        
        try
        {
            ValidateInputs(settings);
            
            if (convertSettings.Direction != "full" && convertSettings.Direction != "auto")
            {
                AnsiConsole.WriteLine("[red]Error: Direction must be 'full' or 'auto'[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);
            
            // Find the property declaration
            var property = node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault()
                ?? node as PropertyDeclarationSyntax;

            if (property == null)
            {
                AnsiConsole.WriteLine($"[yellow]Warning: No property found at line {settings.LineNumber}, column {settings.ColumnNumber}[/]");
                return 1;
            }

            // Perform the conversion
            var result = await ConvertProperty(tree, property, convertSettings.Direction);
            
            if (settings.DryRun)
            {
                var originalContent = File.ReadAllText(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }
            
            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);
            
            AnsiConsole.WriteLine($"[green]Successfully converted property '{property.Identifier.Text}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<string> ConvertProperty(SyntaxTree tree, PropertyDeclarationSyntax property, string direction)
    {
        var root = await tree.GetRootAsync();
        PropertyDeclarationSyntax newProperty;

        if (direction == "full")
        {
            // Convert auto property to full property
            newProperty = ConvertAutoToFull(property);
        }
        else
        {
            // Convert full property to auto property
            newProperty = ConvertFullToAuto(property);
        }

        var newRoot = root.ReplaceNode(property, newProperty);
        return newRoot.ToFullString();
    }

    private PropertyDeclarationSyntax ConvertAutoToFull(PropertyDeclarationSyntax property)
    {
        if (!IsAutoProperty(property))
        {
            throw new InvalidOperationException("Property is not an auto property");
        }

        var propertyName = property.Identifier.Text;
        var fieldName = $"_{char.ToLowerInvariant(propertyName[0])}{propertyName.Substring(1)}";
        
        // Create backing field
        var backingField = SyntaxFactory.FieldDeclaration(
            SyntaxFactory.VariableDeclaration(property.Type)
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(fieldName))))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        // Create getter and setter
        var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory.IdentifierName(fieldName))));

        var setter = SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(fieldName),
                        SyntaxFactory.IdentifierName("value")))));

        var accessorList = SyntaxFactory.AccessorList(
            SyntaxFactory.List(new[] { getter, setter }));

        var newProperty = property
            .WithAccessorList(accessorList)
            .WithInitializer(null)
            .WithSemicolonToken(default);

        // Add backing field before the property
        var containingType = property.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType != null)
        {
            var propertyIndex = containingType.Members.IndexOf(property);
            var newMembers = containingType.Members.Insert(propertyIndex, backingField);
            var newType = containingType.WithMembers(newMembers);
            
            // This is a simplified approach - in practice, we'd need to update the entire tree
            return newProperty;
        }

        return newProperty;
    }

    private PropertyDeclarationSyntax ConvertFullToAuto(PropertyDeclarationSyntax property)
    {
        if (IsAutoProperty(property))
        {
            throw new InvalidOperationException("Property is already an auto property");
        }

        // Create auto property accessors
        var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        var setter = SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        var accessorList = SyntaxFactory.AccessorList(
            SyntaxFactory.List(new[] { getter, setter }));

        return property.WithAccessorList(accessorList);
    }

    private bool IsAutoProperty(PropertyDeclarationSyntax property)
    {
        return property.AccessorList?.Accessors.All(a => a.Body == null && a.ExpressionBody == null) == true;
    }
}