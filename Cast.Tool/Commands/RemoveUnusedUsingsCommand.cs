using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public sealed class RemoveUnusedUsingsSettings : CommandSettings
{
    [Description("Path to the C# source file")]
    [CommandArgument(0, "<file>")]
    public string FilePath { get; init; } = "";

    [Description("Output file path (default: overwrite input)")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    [Description("Preview changes without applying them")]
    [CommandOption("--dry-run")]
    public bool DryRun { get; init; }
}

public sealed class RemoveUnusedUsingsCommand : Command<RemoveUnusedUsingsSettings>
{
    public override int Execute(CommandContext context, RemoveUnusedUsingsSettings settings)
    {
        try
        {
            if (!File.Exists(settings.FilePath))
            {
                AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                return 1;
            }

            var sourceText = File.ReadAllText(settings.FilePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetCompilationUnitRoot();

            // Create a compilation to analyze semantic model
            var compilation = CSharpCompilation.Create("temp")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);

            var semanticModel = compilation.GetSemanticModel(tree);

            // Get all using directives
            var usingDirectives = root.Usings.ToList();
            var unusedUsings = new List<UsingDirectiveSyntax>();

            foreach (var usingDirective in usingDirectives)
            {
                if (IsUsingUnused(usingDirective, root, semanticModel))
                {
                    unusedUsings.Add(usingDirective);
                }
            }

            if (unusedUsings.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No unused using statements found[/]");
                return 0;
            }

            // Remove unused usings
            var newRoot = root.RemoveNodes(unusedUsings, SyntaxRemoveOptions.KeepNoTrivia);
            if (newRoot == null)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to remove unused usings[/]");
                return 1;
            }

            var newSourceText = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = File.ReadAllText(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, newSourceText, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            File.WriteAllText(outputPath, newSourceText);

            AnsiConsole.MarkupLine("[green]Successfully removed {0} unused using statements from {1}[/]", 
                unusedUsings.Count, outputPath);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
            return 1;
        }
    }

    private static bool IsUsingUnused(UsingDirectiveSyntax usingDirective, CompilationUnitSyntax root, SemanticModel semanticModel)
    {
        if (usingDirective.Name == null) return false;

        var namespaceName = usingDirective.Name.ToString();
        
        // Skip system usings that are commonly used implicitly
        if (namespaceName == "System" || namespaceName == "System.Collections.Generic" || 
            namespaceName == "System.Linq" || namespaceName == "System.Threading.Tasks")
        {
            return false; // Conservative approach - don't remove common system usings
        }

        // Simple heuristic: check if any identifiers in the code could reference this namespace
        var walker = new NamespaceUsageWalker(namespaceName);
        walker.Visit(root);
        
        return !walker.IsUsed;
    }

    private class NamespaceUsageWalker : CSharpSyntaxWalker
    {
        private readonly string _targetNamespace;
        public bool IsUsed { get; private set; }

        public NamespaceUsageWalker(string targetNamespace)
        {
            _targetNamespace = targetNamespace;
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Simple heuristic: if we find types that could be from the namespace, mark as used
            var identifier = node.Identifier.ValueText;
            
            // Check if this could be a type from the target namespace
            if (_targetNamespace.Contains(identifier) || 
                CouldBeTypeFromNamespace(identifier, _targetNamespace))
            {
                IsUsed = true;
            }

            base.VisitIdentifierName(node);
        }

        public override void VisitQualifiedName(QualifiedNameSyntax node)
        {
            if (node.ToString().StartsWith(_targetNamespace))
            {
                IsUsed = true;
            }
            base.VisitQualifiedName(node);
        }

        private static bool CouldBeTypeFromNamespace(string identifier, string namespaceName)
        {
            // Simple heuristic: if identifier starts with capital letter, could be a type
            return char.IsUpper(identifier[0]);
        }
    }
}