using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
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
        var renameSettings = settings;
        
        try
        {
            ValidateInputs(settings);
            
            if (string.IsNullOrWhiteSpace(renameSettings.OldName))
            {
                AnsiConsole.WriteLine("[red]Error: Old name is required[/]");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(renameSettings.NewName))
            {
                AnsiConsole.WriteLine("[red]Error: New name is required[/]");
                return 1;
            }

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

            if (symbol.Name != renameSettings.OldName)
            {
                AnsiConsole.WriteLine($"[yellow]Warning: Found symbol '{symbol.Name}' but expected '{renameSettings.OldName}'[/]");
                return 1;
            }

            // Perform the rename to get the modified content
            var result = await PerformSimpleRename(settings.FilePath, renameSettings.OldName, renameSettings.NewName);
            
            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }
            
            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);
            
            AnsiConsole.WriteLine($"[green]Successfully renamed '{renameSettings.OldName}' to '{renameSettings.NewName}' in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
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