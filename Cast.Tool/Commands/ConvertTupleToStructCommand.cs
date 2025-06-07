using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

[Description("Convert tuple to struct")]
public class ConvertTupleToStructCommand : Command<ConvertTupleToStructCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandArgument(1, "<struct-name>")]
        [Description("Name for the new struct")]
        public string StructName { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the refactoring should be applied")]
        [DefaultValue(1)]
        public int Line { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the refactoring should be applied")]
        [DefaultValue(0)]
        public int Column { get; init; } = 0;

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
        try
        {
            if (!File.Exists(settings.FilePath))
            {
                AnsiConsole.MarkupLine($"[red]Error: File not found: {settings.FilePath}[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var task = ExecuteAsync(engine, settings);
            return task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<int> ExecuteAsync(RefactoringEngine engine, Settings settings)
    {
        var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);
        
        var position = engine.GetTextSpanFromPosition(tree, settings.Line, settings.Column);
        var root = await tree.GetRootAsync();
        var node = root.FindNode(position);

        // Find tuple expression or declaration at specified location
        var tupleNode = node as TupleExpressionSyntax ?? node as TupleTypeSyntax ??
                       node.Ancestors().FirstOrDefault(n => n is TupleExpressionSyntax or TupleTypeSyntax);

        if (tupleNode == null)
        {
            AnsiConsole.MarkupLine($"[red]Error: No tuple found at line {settings.Line}, column {settings.Column}[/]");
            return 1;
        }

        SyntaxNode newRoot;
        string structDeclaration;

        if (tupleNode is TupleExpressionSyntax tupleExpr)
        {
            // Convert tuple expression to struct
            var arguments = tupleExpr.Arguments;
            var members = new List<string>();
            var constructorParams = new List<string>();
            var constructorAssignments = new List<string>();

            for (int i = 0; i < arguments.Count; i++)
            {
                var arg = arguments[i];
                var fieldName = arg.NameColon?.Name.Identifier.ValueText ?? $"Item{i + 1}";
                
                // Try to infer type from the semantic model
                var typeInfo = model.GetTypeInfo(arg.Expression);
                var fieldType = typeInfo.Type?.ToDisplayString() ?? "object";
                
                members.Add($"    public {fieldType} {fieldName} {{ get; set; }}");
                constructorParams.Add($"{fieldType} {fieldName.ToLowerInvariant()}");
                constructorAssignments.Add($"        this.{fieldName} = {fieldName.ToLowerInvariant()};");
            }

            structDeclaration = $@"public struct {settings.StructName}
{{
{string.Join(Environment.NewLine, members)}

    public {settings.StructName}({string.Join(", ", constructorParams)})
    {{
{string.Join(Environment.NewLine, constructorAssignments)}
    }}
}}";

            // Replace tuple expression with struct constructor
            var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                tupleExpr.Arguments.Select(arg => SyntaxFactory.Argument(arg.Expression))));
            
            var structCreation = SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.IdentifierName(settings.StructName))
                .WithArgumentList(argumentList)
                .WithLeadingTrivia(tupleExpr.GetLeadingTrivia())
                .WithTrailingTrivia(tupleExpr.GetTrailingTrivia());

            newRoot = root.ReplaceNode(tupleExpr, structCreation);
        }
        else if (tupleNode is TupleTypeSyntax tupleType)
        {
            // Convert tuple type to struct
            var elements = tupleType.Elements;
            var members = new List<string>();
            var constructorParams = new List<string>();
            var constructorAssignments = new List<string>();

            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                var fieldName = element.Identifier.ValueText;
                if (string.IsNullOrEmpty(fieldName))
                    fieldName = $"Item{i + 1}";
                
                var fieldType = element.Type.ToString();
                
                members.Add($"    public {fieldType} {fieldName} {{ get; set; }}");
                constructorParams.Add($"{fieldType} {fieldName.ToLowerInvariant()}");
                constructorAssignments.Add($"        this.{fieldName} = {fieldName.ToLowerInvariant()};");
            }

            structDeclaration = $@"public struct {settings.StructName}
{{
{string.Join(Environment.NewLine, members)}

    public {settings.StructName}({string.Join(", ", constructorParams)})
    {{
{string.Join(Environment.NewLine, constructorAssignments)}
    }}
}}";

            // Replace tuple type with struct name
            var structType = SyntaxFactory.IdentifierName(settings.StructName)
                .WithLeadingTrivia(tupleType.GetLeadingTrivia())
                .WithTrailingTrivia(tupleType.GetTrailingTrivia());
            newRoot = root.ReplaceNode(tupleType, structType);
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error: Unsupported tuple format[/]");
            return 1;
        }

        // Add struct declaration to the compilation unit
        var structSyntax = SyntaxFactory.ParseMemberDeclaration(structDeclaration);
        if (structSyntax != null)
        {
            newRoot = ((CompilationUnitSyntax)newRoot).AddMembers(structSyntax);
        }

        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine($"[green]Would convert tuple to struct '{settings.StructName}' in {settings.FilePath}[/]");
            AnsiConsole.WriteLine(newRoot.ToFullString());
            return 0;
        }

        var outputPath = settings.OutputPath ?? settings.FilePath;
        await File.WriteAllTextAsync(outputPath, newRoot.ToFullString());

        AnsiConsole.MarkupLine($"[green]Successfully converted tuple to struct '{settings.StructName}' in {outputPath}[/]");
        return 0;
    }
}