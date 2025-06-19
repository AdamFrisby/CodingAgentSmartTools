using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class AddConstructorParametersCommand : Command<AddConstructorParametersCommand.Settings>
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

        [CommandOption("--member-names")]
        [Description("Comma-separated list of member names to add as constructor parameters")]
        public string? MemberNames { get; init; }

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

            var memberNames = settings.MemberNames?.Split(',').Select(s => s.Trim()).ToList() ?? new List<string>();
            
            // If no specific members provided, find all auto-properties and fields
            if (!memberNames.Any())
            {
                memberNames = GetAllMembersForConstructor(classDeclaration);
            }

            if (!memberNames.Any())
            {
                AnsiConsole.WriteLine("[yellow]No suitable members found to add as constructor parameters[/]");
                return 0;
            }

            // Create or update the constructor
            var newClassDeclaration = AddOrUpdateConstructor(classDeclaration, memberNames);
            var newRoot = root.ReplaceNode(classDeclaration, newClassDeclaration);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully added constructor parameters for members: {string.Join(", ", memberNames)}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private List<string> GetAllMembersForConstructor(ClassDeclarationSyntax classDeclaration)
    {
        var members = new List<string>();

        // Find auto-properties
        var autoProperties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(p => p.AccessorList?.Accessors.Any(a => a.Body == null && a.ExpressionBody == null) == true)
            .Select(p => p.Identifier.ValueText);

        // Find fields (excluding constants and static fields)
        var fields = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(f => !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword) || m.IsKind(SyntaxKind.StaticKeyword)))
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.ValueText));

        members.AddRange(autoProperties);
        members.AddRange(fields);

        return members;
    }

    private ClassDeclarationSyntax AddOrUpdateConstructor(ClassDeclarationSyntax classDeclaration, List<string> memberNames)
    {
        // Find existing constructor or create new one
        var existingConstructor = classDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();

        var className = classDeclaration.Identifier.ValueText;
        var parameters = new List<ParameterSyntax>();
        var assignments = new List<StatementSyntax>();

        foreach (var memberName in memberNames)
        {
            var parameterName = char.ToLower(memberName[0]) + memberName.Substring(1);
            var memberType = GetMemberType(classDeclaration, memberName) ?? "object";

            // Create parameter with proper spacing
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                .WithType(SyntaxFactory.IdentifierName(memberType).WithTrailingTrivia(SyntaxFactory.Space));
            parameters.Add(parameter);

            // Create assignment statement with proper formatting
            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ThisExpression(),
                        SyntaxFactory.IdentifierName(memberName)),
                    SyntaxFactory.IdentifierName(parameterName))
                .WithOperatorToken(SyntaxFactory.Token(SyntaxKind.EqualsToken)
                    .WithLeadingTrivia(SyntaxFactory.Space)
                    .WithTrailingTrivia(SyntaxFactory.Space)))
                .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
                .WithTrailingTrivia(SyntaxFactory.LineFeed);
            assignments.Add(assignment);
        }

        // Create parameter list with proper spacing
        var parameterList = SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(parameters, 
                Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), parameters.Count - 1)));

        var newConstructor = SyntaxFactory.ConstructorDeclaration(className)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
            .WithParameterList(parameterList)
            .WithBody(SyntaxFactory.Block(assignments)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(SyntaxFactory.LineFeed).WithTrailingTrivia(SyntaxFactory.LineFeed))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(SyntaxFactory.Whitespace("    ")).WithTrailingTrivia(SyntaxFactory.LineFeed)))
            .WithLeadingTrivia(SyntaxFactory.Whitespace("    "))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        if (existingConstructor != null)
        {
            // Update existing constructor
            return classDeclaration.ReplaceNode(existingConstructor, newConstructor);
        }
        else
        {
            // Add new constructor
            var members = classDeclaration.Members.ToList();
            members.Insert(0, newConstructor);
            return classDeclaration.WithMembers(SyntaxFactory.List(members));
        }
    }

    private string? GetMemberType(ClassDeclarationSyntax classDeclaration, string memberName)
    {
        // Try to find the type from properties
        var property = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.ValueText == memberName);

        if (property != null)
        {
            return property.Type.ToString();
        }

        // Try to find the type from fields
        var field = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == memberName));

        if (field != null)
        {
            return field.Declaration.Type.ToString();
        }

        return null;
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