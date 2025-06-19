using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class MoveTypeToMatchingFileCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--type-name")]
        [Description("Name of the type to move to its own file")]
        public string TypeName { get; set; } = string.Empty;

        [CommandOption("--target-directory")]
        [Description("Target directory for the new file (default: same directory as source)")]
        public string? TargetDirectory { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class MoveTypeToMatchingFileCommand : Command<MoveTypeToMatchingFileCommandSettings>
    {
        public override int Execute(CommandContext context, MoveTypeToMatchingFileCommandSettings settings)
        {
            try
            {
                if (!File.Exists(settings.FilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.TypeName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Type name is required[/]");
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var (modifiedRoot, extractedTypeCode) = ExtractType(root, settings.TypeName);

                var targetDirectory = settings.TargetDirectory ?? Path.GetDirectoryName(settings.FilePath) ?? ".";
                var newFileName = Path.Combine(targetDirectory, $"{settings.TypeName}.cs");

                var modifiedCode = modifiedRoot.ToFullString();

                if (settings.DryRun)
                {
                    var originalContent = File.ReadAllText(settings.FilePath);
                    AnsiConsole.MarkupLine("[green]Would move type '{0}' to {1}[/]", 
                        settings.TypeName, newFileName);
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Changes to original file:[/]");
                    DiffUtility.DisplayDiff(originalContent, modifiedCode, settings.FilePath);
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]New file would be created: {0}[/]", newFileName);
                    return 0;
                }

                // Write the extracted type to new file
                File.WriteAllText(newFileName, extractedTypeCode);

                // Update the original file
                File.WriteAllText(settings.FilePath, modifiedCode);

                AnsiConsole.MarkupLine("[green]Successfully moved type '{0}' to {1}[/]", 
                    settings.TypeName, newFileName);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static (CompilationUnitSyntax modifiedRoot, string extractedTypeCode) ExtractType(
            CompilationUnitSyntax root,
            string typeName)
        {
            // Find the type to extract
            var typeDeclaration = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.ValueText == typeName);

            if (typeDeclaration == null)
            {
                throw new InvalidOperationException($"Type '{typeName}' not found");
            }

            // Create a new compilation unit for the extracted type
            var newRoot = SyntaxFactory.CompilationUnit();

            // Copy using directives
            newRoot = newRoot.WithUsings(root.Usings);

            // Check if the type is inside a namespace
            var namespaceDeclaration = typeDeclaration.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
            
            if (namespaceDeclaration != null)
            {
                // Create a new namespace with only the extracted type
                var newNamespace = SyntaxFactory.NamespaceDeclaration(namespaceDeclaration.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDeclaration));
                
                newRoot = newRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNamespace));
            }
            else
            {
                // Type is not in a namespace, add it directly
                newRoot = newRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDeclaration));
            }

            // Remove the type from the original root
            var modifiedRoot = root.RemoveNode(typeDeclaration, SyntaxRemoveOptions.KeepNoTrivia);

            // If the namespace becomes empty after removing the type, remove it too
            if (namespaceDeclaration != null && modifiedRoot != null)
            {
                var updatedNamespace = modifiedRoot.DescendantNodes()
                    .OfType<NamespaceDeclarationSyntax>()
                    .FirstOrDefault(n => n.Name.ToString() == namespaceDeclaration.Name.ToString());

                if (updatedNamespace != null && !updatedNamespace.Members.Any())
                {
                    modifiedRoot = modifiedRoot.RemoveNode(updatedNamespace, SyntaxRemoveOptions.KeepNoTrivia);
                }
            }

            var extractedTypeCode = newRoot.NormalizeWhitespace().ToFullString();
            
            return (modifiedRoot ?? SyntaxFactory.CompilationUnit(), extractedTypeCode);
        }
    }
}