using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.FindSymbols;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class RenameCommand : Command<RenameCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandArgument(1, "<OLD_NAME>")]
        [Description("Current name of the symbol to rename")]
        public string OldName { get; init; } = string.Empty;

        [CommandArgument(2, "<NEW_NAME>")]
        [Description("New name for the symbol")]
        public string NewName { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the refactoring should be applied")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the refactoring should be applied")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-o|--output")]
        [Description("Output file path (defaults to overwriting the input file)")]
        public string? OutputPath { get; init; }

        [CommandOption("--project-path")]
        [Description("Path to the project directory (for project-wide semantic renaming)")]
        public string? ProjectPath { get; init; }

        [CommandOption("--project-wide")]
        [Description("Perform project-wide semantic renaming (finds and updates all references)")]
        [DefaultValue(false)]
        public bool ProjectWide { get; init; } = false;

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
            
            if (string.IsNullOrWhiteSpace(settings.OldName))
            {
                AnsiConsole.WriteLine("[red]Error: Old name is required[/]");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(settings.NewName))
            {
                AnsiConsole.WriteLine("[red]Error: New name is required[/]");
                return 1;
            }

            if (settings.ProjectWide)
            {
                return await PerformProjectWideRename(settings);
            }
            else
            {
                return await PerformSingleFileRename(settings);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<int> PerformSingleFileRename(Settings settings)
    {
        var engine = new RefactoringEngine();
        var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);
        
        var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
        var root = await tree.GetRootAsync();
        var node = root.FindNode(position);
        
        // Find the symbol at the specified position
        var symbol = model.GetSymbolInfo(node).Symbol;
        if (symbol == null)
        {
            // Try to get declared symbol if it's a declaration
            symbol = model.GetDeclaredSymbol(node);
        }

        if (symbol == null)
        {
            AnsiConsole.WriteLine($"[yellow]Warning: No symbol found at line {settings.LineNumber}, column {settings.ColumnNumber}[/]");
            return 1;
        }

        if (symbol.Name != settings.OldName)
        {
            AnsiConsole.WriteLine($"[yellow]Warning: Found symbol '{symbol.Name}' but expected '{settings.OldName}'[/]");
            return 1;
        }

        // Perform the rename to get the modified content
        var result = await PerformSimpleRename(settings.FilePath, settings.OldName, settings.NewName);
        
        if (settings.DryRun)
        {
            var originalContent = await File.ReadAllTextAsync(settings.FilePath);
            DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
            return 0;
        }
        
        var outputPath = settings.OutputPath ?? settings.FilePath;
        await File.WriteAllTextAsync(outputPath, result);
        
        AnsiConsole.WriteLine($"[green]Successfully renamed '{settings.OldName}' to '{settings.NewName}' in {outputPath}[/]");
        return 0;
    }

    private async Task<int> PerformProjectWideRename(Settings settings)
    {
        var projectPath = RefactoringEngine.ResolveProjectPath(settings.FilePath, settings.ProjectPath);
        if (projectPath == null)
        {
            AnsiConsole.WriteLine("[red]Error: Could not find project directory. Use --project-path to specify explicitly.[/]");
            return 1;
        }

        AnsiConsole.WriteLine($"[blue]Using project path: {projectPath}[/]");

        // Find all C# files in the project
        var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("bin") && !f.Contains("obj"))
            .ToList();

        var targetFilePath = Path.GetFullPath(settings.FilePath);
        if (!csFiles.Any(f => Path.GetFullPath(f) == targetFilePath))
        {
            AnsiConsole.WriteLine("[red]Error: Specified file is not part of the detected project.[/]");
            AnsiConsole.WriteLine($"[yellow]Looking for: {targetFilePath}[/]");
            AnsiConsole.WriteLine($"[yellow]Found files: {string.Join(", ", csFiles.Select(Path.GetFullPath))}[/]");
            return 1;
        }

        AnsiConsole.WriteLine($"[blue]Found {csFiles.Count} C# files in project[/]");

        // Build workspace with all project files
        var workspace = await CreateWorkspaceAsync(projectPath, csFiles);
        var project = workspace.CurrentSolution.Projects.First();
        
        // Find the target document and symbol
        var targetDocument = project.Documents.FirstOrDefault(d => 
            d.FilePath != null && Path.GetFullPath(d.FilePath) == targetFilePath);
            
        if (targetDocument == null)
        {
            AnsiConsole.WriteLine("[red]Error: Could not find target file in workspace.[/]");
            AnsiConsole.WriteLine($"[yellow]Looking for: {targetFilePath}[/]");
            AnsiConsole.WriteLine($"[yellow]Documents in workspace: {string.Join(", ", project.Documents.Select(d => d.FilePath ?? "null"))}[/]");
            return 1;
        }

        var syntaxTree = await targetDocument.GetSyntaxTreeAsync();
        var semanticModel = await targetDocument.GetSemanticModelAsync();
        
        if (syntaxTree == null || semanticModel == null)
        {
            AnsiConsole.WriteLine("[red]Error: Could not load semantic model for target file.[/]");
            return 1;
        }

        var position = new RefactoringEngine().GetTextSpanFromPosition(syntaxTree, settings.LineNumber, settings.ColumnNumber);
        var root = await syntaxTree.GetRootAsync();
        var node = root.FindNode(position);
        
        // Find the symbol at the specified position
        var symbol = semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol == null)
        {
            symbol = semanticModel.GetDeclaredSymbol(node);
        }

        if (symbol == null)
        {
            AnsiConsole.WriteLine($"[yellow]Warning: No symbol found at line {settings.LineNumber}, column {settings.ColumnNumber}[/]");
            return 1;
        }

        if (symbol.Name != settings.OldName)
        {
            AnsiConsole.WriteLine($"[yellow]Warning: Found symbol '{symbol.Name}' but expected '{settings.OldName}'[/]");
            return 1;
        }

        AnsiConsole.WriteLine($"[blue]Found symbol: {symbol.Name} of type {symbol.Kind}[/]");

        // Find all references to the symbol across the project
        var references = await SymbolFinder.FindReferencesAsync(symbol, workspace.CurrentSolution);
        var changedFiles = new Dictionary<string, (string original, string modified)>();

        // Process each file that contains references
        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                var document = workspace.CurrentSolution.GetDocument(location.Document.Id);
                if (document?.FilePath == null) continue;

                var filePath = Path.GetFullPath(document.FilePath);
                
                if (!changedFiles.ContainsKey(filePath))
                {
                    var originalContent = await File.ReadAllTextAsync(filePath);
                    changedFiles[filePath] = (originalContent, originalContent);
                }

                // Apply simple text replacement for now
                // In a more sophisticated implementation, this would use Roslyn's rename service
                var (original, current) = changedFiles[filePath];
                var modified = current.Replace(settings.OldName, settings.NewName);
                changedFiles[filePath] = (original, modified);
            }
        }

        if (changedFiles.Count == 0)
        {
            AnsiConsole.WriteLine($"[yellow]No references to '{settings.OldName}' found in the project.[/]");
            return 0;
        }

        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine($"[green]Would rename '{settings.OldName}' to '{settings.NewName}' across {changedFiles.Count} file(s)[/]");
            AnsiConsole.WriteLine();
            DiffUtility.DisplayMultiFileDiff(changedFiles);
            return 0;
        }

        // Apply changes to all files
        foreach (var (filePath, (_, modified)) in changedFiles)
        {
            await File.WriteAllTextAsync(filePath, modified);
        }

        AnsiConsole.WriteLine($"[green]Successfully renamed '{settings.OldName}' to '{settings.NewName}' across {changedFiles.Count} file(s)[/]");
        return 0;
    }

    private async Task<string> PerformSimpleRename(string filePath, string oldName, string newName)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(content);
        var root = await tree.GetRootAsync();
        
        // Create a rewriter to rename identifiers
        var rewriter = new RenameRewriter(oldName, newName);
        var newRoot = rewriter.Visit(root);
        
        return newRoot.ToFullString();
    }

    private async Task<Microsoft.CodeAnalysis.Workspace> CreateWorkspaceAsync(string projectPath, List<string> csFiles)
    {
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var projectId = Microsoft.CodeAnalysis.ProjectId.CreateNewId();
        
        var projectInfo = Microsoft.CodeAnalysis.ProjectInfo.Create(
            projectId,
            Microsoft.CodeAnalysis.VersionStamp.Create(),
            "TempProject",
            "TempProject",
            Microsoft.CodeAnalysis.LanguageNames.CSharp,
            metadataReferences: GetMetadataReferences(),
            compilationOptions: new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        var project = workspace.AddProject(projectInfo);

        // Add all C# files to the project
        foreach (var csFile in csFiles)
        {
            var sourceText = await File.ReadAllTextAsync(csFile);
            var documentId = Microsoft.CodeAnalysis.DocumentId.CreateNewId(projectId);
            
            project = project.AddDocument(
                name: Path.GetFileName(csFile),
                text: sourceText,
                filePath: csFile).Project;
        }

        // Update the workspace with the final project
        workspace.TryApplyChanges(project.Solution);
        
        return workspace;
    }

    private static IEnumerable<Microsoft.CodeAnalysis.MetadataReference> GetMetadataReferences()
    {
        var references = new List<Microsoft.CodeAnalysis.MetadataReference>();
        
        // Add basic .NET references
        var dotnetAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (dotnetAssemblyPath != null)
        {
            var assemblyFiles = new[]
            {
                "System.Runtime.dll",
                "System.Private.CoreLib.dll",
                "System.Console.dll",
                "System.Collections.dll",
                "System.Linq.dll",
                "System.Text.RegularExpressions.dll",
                "System.Threading.dll"
            };

            foreach (var assemblyFile in assemblyFiles)
            {
                var path = Path.Combine(dotnetAssemblyPath, assemblyFile);
                if (File.Exists(path))
                {
                    references.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
                }
            }
        }

        return references;
    }

    private class RenameRewriter : CSharpSyntaxRewriter
    {
        private readonly string _oldName;
        private readonly string _newName;

        public RenameRewriter(string oldName, string newName)
        {
            _oldName = oldName;
            _newName = newName;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(_newName));
            }
            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(_newName));
            }
            return base.VisitVariableDeclarator(node);
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(_newName));
            }
            return base.VisitParameter(node);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(_newName));
            }
            return base.VisitMethodDeclaration(node);
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(_newName));
            }
            return base.VisitClassDeclaration(node);
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Identifier.Text == _oldName)
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(_newName));
            }
            return base.VisitPropertyDeclaration(node);
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
    }
}