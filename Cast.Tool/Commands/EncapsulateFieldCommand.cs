using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class EncapsulateFieldCommand : Command<EncapsulateFieldCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the field is defined")]
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

            // Find the field declaration
            var fieldDeclaration = node.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().FirstOrDefault();
            if (fieldDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No field declaration found at the specified location[/]");
                return 1;
            }

            var variableDeclarator = fieldDeclaration.Declaration.Variables.FirstOrDefault();
            if (variableDeclarator == null)
            {
                AnsiConsole.WriteLine("[red]Error: No variable found in field declaration[/]");
                return 1;
            }

            var fieldName = variableDeclarator.Identifier.ValueText;
            var propertyName = char.ToUpper(fieldName[0]) + fieldName.Substring(1);

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would encapsulate field '{fieldName}' as property '{propertyName}'[/]");
                return 0;
            }

            // Create a private field with underscore prefix
            var backingFieldName = "_" + fieldName;
            var newFieldDeclaration = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(fieldDeclaration.Declaration.Type)
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(backingFieldName))
                            .WithInitializer(variableDeclarator.Initializer))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space)));

            // Create the property
            var propertyDeclaration = SyntaxFactory.PropertyDeclaration(
                fieldDeclaration.Declaration.Type,
                SyntaxFactory.Identifier(propertyName))
                .WithModifiers(fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) 
                    ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                    : SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                .WithAccessorList(SyntaxFactory.AccessorList(
                    SyntaxFactory.List(new[]
                    {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithBody(SyntaxFactory.Block(
                                SyntaxFactory.ReturnStatement(
                                    SyntaxFactory.IdentifierName(backingFieldName)))),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithBody(SyntaxFactory.Block(
                                SyntaxFactory.ExpressionStatement(
                                    SyntaxFactory.AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        SyntaxFactory.IdentifierName(backingFieldName),
                                        SyntaxFactory.IdentifierName("value")))))
                    })));

            // Find the containing type
            var containingType = fieldDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (containingType == null)
            {
                AnsiConsole.WriteLine("[red]Error: Field must be inside a class or struct[/]");
                return 1;
            }

            // Replace the field with the backing field and add the property
            var newContainingType = containingType
                .ReplaceNode(fieldDeclaration, newFieldDeclaration)
                .AddMembers(propertyDeclaration);

            var newRoot = root.ReplaceNode(containingType, newContainingType);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully encapsulated field '{fieldName}' as property '{propertyName}' in {outputPath}[/]");
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