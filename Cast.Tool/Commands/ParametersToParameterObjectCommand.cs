using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ParametersToParameterObjectCommand : Command<ParametersToParameterObjectCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file containing the method to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the method is declared")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the method starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-n|--parameter-object-name")]
        [Description("Name for the parameter object class/struct/record")]
        public string? ParameterObjectName { get; init; }

        [CommandOption("-t|--parameter-object-type")]
        [Description("Type of parameter object: 'class', 'struct', or 'record'")]
        [DefaultValue("class")]
        public string ParameterObjectType { get; init; } = "class";

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

            // Validate method has parameters
            if (method.ParameterList.Parameters.Count == 0)
            {
                AnsiConsole.WriteLine("[yellow]Warning: Method has no parameters to convert[/]");
                return 1;
            }

            // Check for unsupported parameter types (ref, out, in, params)
            if (method.ParameterList.Parameters.Any(p => 
                p.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword) || 
                                   m.IsKind(SyntaxKind.OutKeyword) || 
                                   m.IsKind(SyntaxKind.InKeyword) ||
                                   m.IsKind(SyntaxKind.ParamsKeyword))))
            {
                AnsiConsole.WriteLine("[red]Error: Methods with ref, out, in, or params parameters are not supported[/]");
                return 1;
            }

            // Generate parameter object name if not provided
            var parameterObjectName = settings.ParameterObjectName 
                ?? GenerateParameterObjectName(method.Identifier.ValueText);

            // Create the parameter object type
            var parameterObject = CreateParameterObject(method, parameterObjectName, settings.ParameterObjectType);

            // Update method signature to use parameter object
            var updatedMethod = UpdateMethodSignature(method, parameterObjectName);

            // Update method body to use parameter object
            var updatedMethodWithBody = UpdateMethodBody(updatedMethod, method.ParameterList.Parameters);

            // Find containing type and update it
            var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (containingType == null)
            {
                AnsiConsole.WriteLine("[red]Error: Method must be inside a class, struct, or record[/]");
                return 1;
            }

            // Add parameter object to containing type and update method
            var updatedContainingType = containingType
                .ReplaceNode(method, updatedMethodWithBody)
                .AddMembers(parameterObject);

            var newRoot = root.ReplaceNode(containingType, updatedContainingType);

            // TODO: Find and update all call sites (for now, just show a warning)
            AnsiConsole.WriteLine("[yellow]Note: Call sites will need to be updated manually in this version[/]");

            var result = newRoot.NormalizeWhitespace().ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully converted method '{method.Identifier.ValueText}' to use parameter object '{parameterObjectName}' in {outputPath}[/]");
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

        var validTypes = new[] { "class", "struct", "record" };
        if (!validTypes.Contains(settings.ParameterObjectType.ToLower()))
        {
            throw new ArgumentException("Parameter object type must be 'class', 'struct', or 'record'");
        }
    }

    private string GenerateParameterObjectName(string methodName)
    {
        return $"{methodName}Args";
    }

    private MemberDeclarationSyntax CreateParameterObject(MethodDeclarationSyntax method, string parameterObjectName, string objectType)
    {
        var parameters = method.ParameterList.Parameters;
        
        return objectType.ToLower() switch
        {
            "record" => CreateRecordParameterObject(parameters, parameterObjectName),
            "struct" => CreateStructParameterObject(parameters, parameterObjectName),
            _ => CreateClassParameterObject(parameters, parameterObjectName)
        };
    }

    private RecordDeclarationSyntax CreateRecordParameterObject(SeparatedSyntaxList<ParameterSyntax> parameters, string parameterObjectName)
    {
        var recordParameters = SyntaxFactory.SeparatedList(
            parameters.Select(p => SyntaxFactory.Parameter(p.Identifier)
                .WithType(p.Type)));

        return SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), parameterObjectName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(recordParameters))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }

    private StructDeclarationSyntax CreateStructParameterObject(SeparatedSyntaxList<ParameterSyntax> parameters, string parameterObjectName)
    {
        var properties = parameters.Select(p => 
            SyntaxFactory.PropertyDeclaration(p.Type!, p.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(SyntaxFactory.AccessorList(
                    SyntaxFactory.List(new[]
                    {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    })))
                .NormalizeWhitespace());

        var constructor = CreateConstructor(parameterObjectName, parameters);

        return SyntaxFactory.StructDeclaration(parameterObjectName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(properties.Cast<MemberDeclarationSyntax>().Concat(new[] { constructor })))
            .NormalizeWhitespace();
    }

    private ClassDeclarationSyntax CreateClassParameterObject(SeparatedSyntaxList<ParameterSyntax> parameters, string parameterObjectName)
    {
        var properties = parameters.Select(p => 
            SyntaxFactory.PropertyDeclaration(p.Type!, p.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(SyntaxFactory.AccessorList(
                    SyntaxFactory.List(new[]
                    {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    })))
                .NormalizeWhitespace());

        var constructor = CreateConstructor(parameterObjectName, parameters);

        return SyntaxFactory.ClassDeclaration(parameterObjectName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(properties.Cast<MemberDeclarationSyntax>().Concat(new[] { constructor })))
            .NormalizeWhitespace();
    }

    private ConstructorDeclarationSyntax CreateConstructor(string className, SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var constructorParameters = SyntaxFactory.SeparatedList(parameters);
        
        var assignments = parameters.Select(p =>
            SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(p.Identifier.ValueText),
                    SyntaxFactory.IdentifierName(p.Identifier.ValueText))));

        var body = SyntaxFactory.Block(assignments);

        return SyntaxFactory.ConstructorDeclaration(className)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(constructorParameters))
            .WithBody(body)
            .NormalizeWhitespace();
    }

    private MethodDeclarationSyntax UpdateMethodSignature(MethodDeclarationSyntax method, string parameterObjectName)
    {
        var parameterObjectType = SyntaxFactory.IdentifierName(parameterObjectName);
        var argsParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("args"))
            .WithType(parameterObjectType);

        var newParameterList = SyntaxFactory.ParameterList(
            SyntaxFactory.SingletonSeparatedList(argsParameter))
            .NormalizeWhitespace();

        return method.WithParameterList(newParameterList)
                    .WithLeadingTrivia(method.GetLeadingTrivia())
                    .WithTrailingTrivia(method.GetTrailingTrivia());
    }

    private MethodDeclarationSyntax UpdateMethodBody(MethodDeclarationSyntax method, SeparatedSyntaxList<ParameterSyntax> originalParameters)
    {
        if (method.Body == null)
            return method;

        var parameterNames = originalParameters.Select(p => p.Identifier.ValueText).ToHashSet();
        var updatedBody = method.Body;

        // Create a dictionary to track all replacements
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

        // Find all identifier name references to parameters within the method body
        var identifierNodes = updatedBody.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(id => parameterNames.Contains(id.Identifier.ValueText) && 
                        !IsPartOfMemberAccess(id) && // Don't replace if already part of member access
                        !IsInParameterContext(id)) // Don't replace if in parameter context
            .ToList();

        foreach (var identifier in identifierNodes)
        {
            var parameterName = identifier.Identifier.ValueText;
            var memberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("args"),
                SyntaxFactory.IdentifierName(parameterName));

            replacements[identifier] = memberAccess;
        }

        // Apply all replacements
        updatedBody = updatedBody.ReplaceNodes(replacements.Keys, (original, rewritten) => replacements[original]);

        return method.WithBody(updatedBody);
    }

    private bool IsPartOfMemberAccess(IdentifierNameSyntax identifier)
    {
        // Check if this identifier is already part of a member access expression
        return identifier.Parent is MemberAccessExpressionSyntax;
    }

    private bool IsInParameterContext(IdentifierNameSyntax identifier)
    {
        // Check if this identifier is in a parameter declaration context
        return identifier.Ancestors().OfType<ParameterSyntax>().Any();
    }
}