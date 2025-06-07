using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class SplitOrMergeIfStatementsCommand : Command<SplitOrMergeIfStatementsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the if statement is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("--operation")]
        [Description("Operation to perform: split or merge")]
        [DefaultValue("auto")]
        public string Operation { get; init; } = "auto";

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

            // Find the if statement
            var ifStatement = node.AncestorsAndSelf().OfType<IfStatementSyntax>().FirstOrDefault();
            if (ifStatement == null)
            {
                AnsiConsole.WriteLine("[red]Error: No if statement found at the specified location[/]");
                return 1;
            }

            var operation = DetermineOperation(ifStatement, settings.Operation);

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would {operation} if statement[/]");
                return 0;
            }

            StatementSyntax? newStatement = operation switch
            {
                "split" => SplitIfStatement(ifStatement),
                "merge" => MergeIfStatement(ifStatement),
                _ => null
            };

            if (newStatement == null)
            {
                AnsiConsole.WriteLine($"[red]Error: Could not {operation} if statement[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(ifStatement, newStatement);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully {operation}ed if statement in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static string DetermineOperation(IfStatementSyntax ifStatement, string requestedOperation)
    {
        if (requestedOperation != "auto")
            return requestedOperation;

        // Auto-determine: if condition has && or ||, suggest split; if nested with same body, suggest merge
        if (ifStatement.Condition is BinaryExpressionSyntax binary && 
            (binary.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken) || 
             binary.OperatorToken.IsKind(SyntaxKind.BarBarToken)))
        {
            return "split";
        }

        return "merge";
    }

    private static StatementSyntax? SplitIfStatement(IfStatementSyntax ifStatement)
    {
        // Split if (a && b) into if (a) if (b)
        if (ifStatement.Condition is BinaryExpressionSyntax binary && 
            binary.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken))
        {
            var innerIf = SyntaxFactory.IfStatement(binary.Right, ifStatement.Statement);
            var outerIf = SyntaxFactory.IfStatement(binary.Left, SyntaxFactory.Block(innerIf));
            return outerIf;
        }

        return null;
    }

    private static StatementSyntax? MergeIfStatement(IfStatementSyntax ifStatement)
    {
        // Merge nested if statements with same body into single if with &&
        if (ifStatement.Statement is BlockSyntax block && 
            block.Statements.Count == 1 && 
            block.Statements[0] is IfStatementSyntax innerIf)
        {
            var mergedCondition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                SyntaxFactory.ParenthesizedExpression(ifStatement.Condition),
                SyntaxFactory.ParenthesizedExpression(innerIf.Condition));

            return SyntaxFactory.IfStatement(mergedCondition, innerIf.Statement);
        }

        return null;
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

        if (!new[] { "auto", "split", "merge" }.Contains(settings.Operation.ToLowerInvariant()))
        {
            throw new ArgumentException("Operation must be 'auto', 'split', or 'merge'");
        }
    }
}