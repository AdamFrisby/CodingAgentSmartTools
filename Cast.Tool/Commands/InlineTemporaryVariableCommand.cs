using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class InlineTemporaryVariableCommand : Command<InlineTemporaryVariableCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the variable declaration is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

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

            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, 0);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);

            // Find the variable declaration
            var variableDeclaration = node.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
            if (variableDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No variable declaration found at the specified location[/]");
                return 1;
            }

            var variable = variableDeclaration.Declaration.Variables.FirstOrDefault();
            if (variable?.Initializer?.Value == null)
            {
                AnsiConsole.WriteLine("[red]Error: Variable must have an initializer[/]");
                return 1;
            }

            var variableName = variable.Identifier.ValueText;
            var initializerExpression = variable.Initializer.Value;

            // Find all references to this variable in the same scope
            var containingBlock = variableDeclaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (containingBlock == null)
            {
                AnsiConsole.WriteLine("[red]Error: Variable declaration must be inside a block[/]");
                return 1;
            }

            var referencesToReplace = new List<IdentifierNameSyntax>();
            foreach (var identifierName in containingBlock.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifierName.Identifier.ValueText == variableName && 
                    !identifierName.Ancestors().Contains(variableDeclaration))
                {
                    referencesToReplace.Add(identifierName);
                }
            }

            if (referencesToReplace.Count == 0)
            {
                AnsiConsole.WriteLine($"[yellow]No references to variable '{variableName}' found[/]");
            }

            // Replace all references with the initializer expression
            var newRoot = root;
            foreach (var reference in referencesToReplace)
            {
                newRoot = newRoot.ReplaceNode(reference, initializerExpression.WithTriviaFrom(reference));
            }

            // Remove the variable declaration
            var newContainingBlock = newRoot.DescendantNodes().OfType<BlockSyntax>()
                .FirstOrDefault(b => b.Statements.Any(s => s.IsEquivalentTo(variableDeclaration)));
            
            if (newContainingBlock != null)
            {
                var updatedVariableDeclaration = newContainingBlock.Statements
                    .OfType<LocalDeclarationStatementSyntax>()
                    .FirstOrDefault(v => v.Declaration.Variables.Any(var => var.Identifier.ValueText == variableName));

                if (updatedVariableDeclaration != null)
                {
                    var newBlock = newContainingBlock.RemoveNode(updatedVariableDeclaration, SyntaxRemoveOptions.KeepNoTrivia);
                    if (newBlock != null)
                    {
                        newRoot = newRoot.ReplaceNode(newContainingBlock, newBlock);
                    }
                }
            }

            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully inlined temporary variable '{variableName}' in {outputPath}[/]");
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
    }
}