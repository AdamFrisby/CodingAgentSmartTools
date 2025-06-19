using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class SyncTypeAndFileCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--rename-type")]
        [Description("Rename type to match file name")]
        public bool RenameType { get; set; }

        [CommandOption("--rename-file")]
        [Description("Rename file to match type name (default behavior)")]
        public bool RenameFile { get; set; } = true;

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class SyncTypeAndFileCommand : Command<SyncTypeAndFileCommandSettings>
    {
        public override int Execute(CommandContext context, SyncTypeAndFileCommandSettings settings)
        {
            try
            {
                if (!File.Exists(settings.FilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var fileName = Path.GetFileNameWithoutExtension(settings.FilePath);
                var primaryType = FindPrimaryType(root);

                if (primaryType == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: No primary type found in file[/]");
                    return 1;
                }

                var typeName = primaryType.Identifier.ValueText;

                // Determine what action to take
                if (fileName == typeName)
                {
                    AnsiConsole.MarkupLine("[yellow]File name and type name are already synchronized[/]");
                    return 0;
                }

                if (settings.RenameType && !settings.RenameFile)
                {
                    return RenameTypeToMatchFile(settings, root, primaryType, fileName);
                }
                else
                {
                    return RenameFileToMatchType(settings, typeName);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static TypeDeclarationSyntax? FindPrimaryType(CompilationUnitSyntax root)
        {
            // Find the first public type, or the first type if no public types
            var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();
            
            if (!types.Any())
                return null;

            // Prefer public types
            var publicType = types.FirstOrDefault(t => t.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));
            if (publicType != null)
                return publicType;

            // Otherwise return the first type
            return types.First();
        }

        private static int RenameTypeToMatchFile(
            SyncTypeAndFileCommandSettings settings,
            CompilationUnitSyntax root,
            TypeDeclarationSyntax primaryType,
            string fileName)
        {
            var originalTypeName = primaryType.Identifier.ValueText;
            
            // Create new type with the file name
            var newIdentifier = SyntaxFactory.Identifier(fileName);
            var newType = primaryType.WithIdentifier(newIdentifier);
            
            // Also rename any constructors to match
            var updatedMembers = new List<MemberDeclarationSyntax>();
            foreach (var member in newType.Members)
            {
                if (member is ConstructorDeclarationSyntax constructor && 
                    constructor.Identifier.ValueText == originalTypeName)
                {
                    updatedMembers.Add(constructor.WithIdentifier(newIdentifier));
                }
                else
                {
                    updatedMembers.Add(member);
                }
            }
            
            newType = newType.WithMembers(SyntaxFactory.List(updatedMembers));
            var modifiedRoot = root.ReplaceNode(primaryType, newType);
            var modifiedCode = modifiedRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalCode = File.ReadAllText(settings.FilePath);
                DiffUtility.DisplayDiff(originalCode, modifiedCode, settings.FilePath);
                return 0;
            }

            File.WriteAllText(settings.FilePath, modifiedCode);

            AnsiConsole.MarkupLine("[green]Successfully renamed type '{0}' to '{1}' in {2}[/]", 
                originalTypeName, fileName, settings.FilePath);

            return 0;
        }

        private static int RenameFileToMatchType(
            SyncTypeAndFileCommandSettings settings,
            string typeName)
        {
            var directory = Path.GetDirectoryName(settings.FilePath) ?? ".";
            var extension = Path.GetExtension(settings.FilePath);
            var newFilePath = Path.Combine(directory, $"{typeName}{extension}");

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[green]Would rename file from '{0}' to '{1}'[/]", 
                    Path.GetFileName(settings.FilePath), Path.GetFileName(newFilePath));
                return 0;
            }

            if (File.Exists(newFilePath))
            {
                AnsiConsole.MarkupLine("[red]Error: Target file '{0}' already exists[/]", newFilePath);
                return 1;
            }

            File.Move(settings.FilePath, newFilePath);

            AnsiConsole.MarkupLine("[green]Successfully renamed file from '{0}' to '{1}'[/]", 
                Path.GetFileName(settings.FilePath), Path.GetFileName(newFilePath));

            return 0;
        }
    }
}