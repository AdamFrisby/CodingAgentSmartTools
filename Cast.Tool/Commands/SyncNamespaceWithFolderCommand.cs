using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class SyncNamespaceWithFolderCommand : Command<SyncNamespaceWithFolderCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("--root-namespace")]
        [Description("Root namespace (defaults to project directory name)")]
        public string? RootNamespace { get; init; }

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
            var root = await tree.GetRootAsync();

            // Find the namespace declaration
            var namespaceDeclaration = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()
                ?? root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault() as BaseNamespaceDeclarationSyntax;

            if (namespaceDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No namespace declaration found in the file[/]");
                return 1;
            }

            // Calculate expected namespace based on folder structure
            var expectedNamespace = CalculateExpectedNamespace(settings.FilePath, settings.RootNamespace);
            var currentNamespace = namespaceDeclaration.Name.ToString();

            if (currentNamespace == expectedNamespace)
            {
                AnsiConsole.WriteLine($"[yellow]Namespace '{currentNamespace}' already matches folder structure[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would change namespace from '{currentNamespace}' to '{expectedNamespace}'[/]");
                return 0;
            }

            // Update the namespace
            var newNamespaceName = SyntaxFactory.ParseName(expectedNamespace);
            var newNamespaceDeclaration = namespaceDeclaration.WithName(newNamespaceName);

            var newRoot = root.ReplaceNode(namespaceDeclaration, newNamespaceDeclaration);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully updated namespace from '{currentNamespace}' to '{expectedNamespace}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static string CalculateExpectedNamespace(string filePath, string? rootNamespace)
    {
        var fileInfo = new FileInfo(filePath);
        var projectRoot = FindProjectRoot(fileInfo.Directory!);
        
        // Use provided root namespace or project directory name
        var baseNamespace = rootNamespace ?? projectRoot?.Name ?? "DefaultNamespace";
        
        // Calculate relative path from project root
        var relativePath = projectRoot != null 
            ? Path.GetRelativePath(projectRoot.FullName, fileInfo.Directory!.FullName)
            : fileInfo.Directory!.Name;

        // Build namespace from path segments
        var segments = new List<string> { baseNamespace };
        
        if (relativePath != ".")
        {
            var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(s => !string.IsNullOrEmpty(s) && IsValidNamespaceSegment(s));
            segments.AddRange(pathSegments);
        }

        return string.Join(".", segments);
    }

    private static DirectoryInfo? FindProjectRoot(DirectoryInfo directory)
    {
        var current = directory;
        while (current != null)
        {
            if (current.GetFiles("*.csproj").Any() || current.GetFiles("*.sln").Any())
            {
                return current;
            }
            current = current.Parent;
        }
        return directory; // Fallback to original directory
    }

    private static bool IsValidNamespaceSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return false;

        if (!char.IsLetter(segment[0]) && segment[0] != '_')
            return false;

        return segment.All(c => char.IsLetterOrDigit(c) || c == '_');
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
    }
}