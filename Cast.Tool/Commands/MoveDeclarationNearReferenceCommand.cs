using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class MoveDeclarationNearReferenceCommand : Command<MoveDeclarationNearReferenceCommand.Settings>
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
            if (variable == null)
            {
                AnsiConsole.WriteLine("[red]Error: No variable found in declaration[/]");
                return 1;
            }

            var variableName = variable.Identifier.ValueText;

            // Find the containing block
            var containingBlock = variableDeclaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (containingBlock == null)
            {
                AnsiConsole.WriteLine("[red]Error: Variable declaration must be inside a block[/]");
                return 1;
            }

            // Find first usage of the variable after the declaration
            var statements = containingBlock.Statements.ToList();
            var declarationIndex = statements.IndexOf(variableDeclaration);
            if (declarationIndex == -1)
            {
                AnsiConsole.WriteLine("[red]Error: Could not find declaration in containing block[/]");
                return 1;
            }

            StatementSyntax? firstUsageStatement = null;
            int firstUsageIndex = -1;

            for (int i = declarationIndex + 1; i < statements.Count; i++)
            {
                var statement = statements[i];
                var identifiers = statement.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.ValueText == variableName);

                if (identifiers.Any())
                {
                    firstUsageStatement = statement;
                    firstUsageIndex = i;
                    break;
                }
            }

            if (firstUsageStatement == null || firstUsageIndex <= declarationIndex + 1)
            {
                AnsiConsole.WriteLine($"[yellow]Variable '{variableName}' is already close to its first usage or not used[/]");
                return 0;
            }

            // Move the declaration just before the first usage
            var newStatements = statements.ToList();
            newStatements.RemoveAt(declarationIndex);
            newStatements.Insert(firstUsageIndex - 1, variableDeclaration);

            var newBlock = containingBlock.WithStatements(SyntaxFactory.List(newStatements));
            var newRoot = root.ReplaceNode(containingBlock, newBlock);
            var result = newRoot.ToFullString();

            if (settings.DryRun)
            {
                var originalContent = await File.ReadAllTextAsync(settings.FilePath);
                DiffUtility.DisplayDiff(originalContent, result, settings.FilePath);
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully moved declaration of variable '{variableName}' closer to its first use in {outputPath}[/]");
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