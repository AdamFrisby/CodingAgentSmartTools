using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class MoveTypeToNamespaceFolderCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--type-name")]
        [Description("Name of the type to move")]
        public string TypeName { get; set; } = string.Empty;

        [CommandOption("--target-namespace")]
        [Description("Target namespace for the type")]
        public string TargetNamespace { get; set; } = string.Empty;

        [CommandOption("--target-folder")]
        [Description("Target folder path (default: namespace path relative to project)")]
        public string? TargetFolder { get; set; }

        [CommandOption("--project-path")]
        [Description("Path to the project directory (for resolving target folder structure)")]
        public string? ProjectPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class MoveTypeToNamespaceFolderCommand : Command<MoveTypeToNamespaceFolderCommandSettings>
    {
        public override int Execute(CommandContext context, MoveTypeToNamespaceFolderCommandSettings settings)
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

                if (string.IsNullOrWhiteSpace(settings.TargetNamespace))
                {
                    AnsiConsole.MarkupLine("[red]Error: Target namespace is required[/]");
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var (modifiedRoot, extractedTypeCode) = ExtractTypeWithNamespace(root, settings.TypeName, settings.TargetNamespace);

                var targetFolder = settings.TargetFolder ?? CreateFolderFromNamespace(settings.TargetNamespace, Path.GetDirectoryName(settings.FilePath)!);
                var newFileName = Path.Combine(targetFolder, $"{settings.TypeName}.cs");

                if (settings.DryRun)
                {
                    AnsiConsole.MarkupLine("[green]Would move type '{0}' to namespace '{1}' in {2}[/]", 
                        settings.TypeName, settings.TargetNamespace, newFileName);
                    
                    // Show diff for the original file (with type removed)
                    var dryRunModifiedCode = modifiedRoot.ToFullString();
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Changes to original file:[/]");
                    DiffUtility.DisplayDiff(sourceCode, dryRunModifiedCode, settings.FilePath);
                    
                    // Show the new file that would be created
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]New file would be created: {0}[/]", newFileName);
                    DiffUtility.DisplayDiff("", extractedTypeCode, newFileName);
                    
                    return 0;
                }

                // Create target directory if it doesn't exist
                Directory.CreateDirectory(targetFolder);

                // Write the extracted type to new file
                File.WriteAllText(newFileName, extractedTypeCode);

                // Update the original file
                var modifiedCode = modifiedRoot.ToFullString();
                File.WriteAllText(settings.FilePath, modifiedCode);

                AnsiConsole.MarkupLine("[green]Successfully moved type '{0}' to namespace '{1}' in {2}[/]", 
                    settings.TypeName, settings.TargetNamespace, newFileName);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static (CompilationUnitSyntax modifiedRoot, string extractedTypeCode) ExtractTypeWithNamespace(
            CompilationUnitSyntax root,
            string typeName,
            string targetNamespace)
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

            // Create the target namespace
            var targetNamespaceNode = SyntaxFactory.NamespaceDeclaration(
                SyntaxFactory.ParseName(targetNamespace))
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeDeclaration));

            newRoot = newRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(targetNamespaceNode));

            // Remove the type from the original root
            var modifiedRoot = root.RemoveNode(typeDeclaration, SyntaxRemoveOptions.KeepNoTrivia);

            // If the original namespace becomes empty after removing the type, remove it too
            var originalNamespace = typeDeclaration.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
            if (originalNamespace != null && modifiedRoot != null)
            {
                var updatedNamespace = modifiedRoot.DescendantNodes()
                    .OfType<NamespaceDeclarationSyntax>()
                    .FirstOrDefault(n => n.Name.ToString() == originalNamespace.Name.ToString());

                if (updatedNamespace != null && !updatedNamespace.Members.Any())
                {
                    modifiedRoot = modifiedRoot.RemoveNode(updatedNamespace, SyntaxRemoveOptions.KeepNoTrivia);
                }
            }

            var extractedTypeCode = newRoot.NormalizeWhitespace().ToFullString();
            
            return (modifiedRoot ?? SyntaxFactory.CompilationUnit(), extractedTypeCode);
        }

        private static string CreateFolderFromNamespace(string targetNamespace, string baseDirectory)
        {
            // Convert namespace to folder path (e.g., "MyProject.Services.Data" -> "Services/Data")
            var namespaceParts = targetNamespace.Split('.');
            
            // Skip the first part if it looks like a root project name
            var folderParts = namespaceParts.Length > 1 ? namespaceParts.Skip(1) : namespaceParts;
            
            var relativePath = Path.Combine(folderParts.ToArray());
            return Path.Combine(baseDirectory, relativePath);
        }
    }
}