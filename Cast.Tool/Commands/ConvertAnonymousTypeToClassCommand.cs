using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertAnonymousTypeToClassCommand : Command<ConvertAnonymousTypeToClassCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the anonymous type is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the anonymous type starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-n|--class-name")]
        [Description("Name for the new class")]
        [DefaultValue("GeneratedClass")]
        public string ClassName { get; init; } = "GeneratedClass";

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

            // Find the anonymous object creation expression
            var anonymousObject = node.AncestorsAndSelf().OfType<AnonymousObjectCreationExpressionSyntax>().FirstOrDefault();
            if (anonymousObject == null)
            {
                AnsiConsole.WriteLine("[red]Error: No anonymous object creation found at the specified location[/]");
                return 1;
            }

            // Generate the class declaration
            var classDeclaration = GenerateClassFromAnonymousType(anonymousObject, settings.ClassName);

            // Find the namespace or compilation unit to add the class to
            var namespaceDeclaration = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            var compilationUnit = root as CompilationUnitSyntax;

            SyntaxNode newRoot;
            if (namespaceDeclaration != null)
            {
                // Add class to namespace
                var newNamespace = namespaceDeclaration.AddMembers(classDeclaration);
                newRoot = root.ReplaceNode(namespaceDeclaration, newNamespace);
            }
            else if (compilationUnit != null)
            {
                // Add class to compilation unit
                newRoot = compilationUnit.AddMembers(classDeclaration);
            }
            else
            {
                AnsiConsole.WriteLine("[red]Error: Could not find suitable location to add the new class[/]");
                return 1;
            }

            // Replace the anonymous object with a constructor call
            var constructorCall = SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName(settings.ClassName))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(
                        anonymousObject.Initializers.Select(init =>
                        {
                            if (init is AnonymousObjectMemberDeclaratorSyntax memberDeclarator)
                            {
                                return SyntaxFactory.Argument(memberDeclarator.Expression);
                            }
                            return SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
                        }))))
                .WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space));

            newRoot = newRoot.ReplaceNode(
                newRoot.DescendantNodes().OfType<AnonymousObjectCreationExpressionSyntax>()
                    .First(ao => ao.IsEquivalentTo(anonymousObject)), 
                constructorCall);

            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted anonymous type to class '{settings.ClassName}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private ClassDeclarationSyntax GenerateClassFromAnonymousType(AnonymousObjectCreationExpressionSyntax anonymousObject, string className)
    {
        var properties = new List<PropertyDeclarationSyntax>();
        var constructorParameters = new List<ParameterSyntax>();
        var constructorAssignments = new List<StatementSyntax>();

        foreach (var initializer in anonymousObject.Initializers)
        {
            if (initializer is AnonymousObjectMemberDeclaratorSyntax memberDeclarator)
            {
                var propertyName = memberDeclarator.NameEquals?.Name.Identifier.ValueText ?? 
                                 ExtractPropertyNameFromExpression(memberDeclarator.Expression);
                
                // Create property with proper formatting
                var property = SyntaxFactory.PropertyDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)), 
                        propertyName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .AddAccessorListAccessors(
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                    .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                properties.Add(property);

                // Create constructor parameter
                var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(propertyName.ToLowerInvariant()))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)));
                constructorParameters.Add(parameter);

                // Create constructor assignment
                var assignment = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ThisExpression(),
                            SyntaxFactory.IdentifierName(propertyName)),
                        SyntaxFactory.IdentifierName(propertyName.ToLowerInvariant())))
                    .WithLeadingTrivia(SyntaxFactory.Whitespace("            "));
                constructorAssignments.Add(assignment);
            }
        }

        // Create constructor with proper formatting
        var constructor = SyntaxFactory.ConstructorDeclaration(className)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(constructorParameters)))
            .WithBody(SyntaxFactory.Block(constructorAssignments))
            .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        // Create class with proper formatting
        var classDeclaration = SyntaxFactory.ClassDeclaration(className)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddMembers(properties.Cast<MemberDeclarationSyntax>().ToArray())
            .AddMembers(constructor)
            .WithLeadingTrivia(SyntaxFactory.Whitespace("    "))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        return classDeclaration;
    }

    private string ExtractPropertyNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => "Property"
        };
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

        if (string.IsNullOrWhiteSpace(settings.ClassName))
        {
            throw new ArgumentException("Class name cannot be empty");
        }
    }
}