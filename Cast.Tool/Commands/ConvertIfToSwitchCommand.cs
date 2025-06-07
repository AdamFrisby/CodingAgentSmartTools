using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ConvertIfToSwitchCommand : Command<ConvertIfToSwitchCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the if/switch statement is located")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) where the if/switch statement starts")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-t|--target")]
        [Description("Target conversion: 'switch' to convert if to switch, 'if' to convert switch to if")]
        [DefaultValue("switch")]
        public string Target { get; init; } = "switch";

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

            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);

            if (settings.Target.ToLower() == "switch")
            {
                // Convert if-else-if chain to switch statement
                var ifStatement = node.AncestorsAndSelf().OfType<IfStatementSyntax>().FirstOrDefault();
                if (ifStatement == null)
                {
                    AnsiConsole.WriteLine("[red]Error: No if statement found at the specified location[/]");
                    return 1;
                }

                if (settings.DryRun)
                {
                    AnsiConsole.WriteLine($"[green]Would convert if statement to switch at line {settings.LineNumber}[/]");
                    return 0;
                }

                var switchStatement = ConvertIfToSwitch(ifStatement);
                if (switchStatement == null)
                {
                    AnsiConsole.WriteLine("[red]Error: Could not convert if statement to switch (might not be suitable for conversion)[/]");
                    return 1;
                }

                var newRoot = root.ReplaceNode(ifStatement, switchStatement);
                var result = newRoot.ToFullString();

                var outputPath = settings.OutputPath ?? settings.FilePath;
                await File.WriteAllTextAsync(outputPath, result);

                AnsiConsole.WriteLine($"[green]Successfully converted if statement to switch in {outputPath}[/]");
                return 0;
            }
            else if (settings.Target.ToLower() == "if")
            {
                // Convert switch statement to if-else-if chain
                var switchStatement = node.AncestorsAndSelf().OfType<SwitchStatementSyntax>().FirstOrDefault();
                if (switchStatement == null)
                {
                    AnsiConsole.WriteLine("[red]Error: No switch statement found at the specified location[/]");
                    return 1;
                }

                if (settings.DryRun)
                {
                    AnsiConsole.WriteLine($"[green]Would convert switch statement to if-else-if chain at line {settings.LineNumber}[/]");
                    return 0;
                }

                var ifStatement = ConvertSwitchToIf(switchStatement);
                var newRoot = root.ReplaceNode(switchStatement, ifStatement);
                var result = newRoot.ToFullString();

                var outputPath = settings.OutputPath ?? settings.FilePath;
                await File.WriteAllTextAsync(outputPath, result);

                AnsiConsole.WriteLine($"[green]Successfully converted switch statement to if-else-if chain in {outputPath}[/]");
                return 0;
            }
            else
            {
                AnsiConsole.WriteLine("[red]Error: Target must be either 'switch' or 'if'[/]");
                return 1;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private SwitchStatementSyntax? ConvertIfToSwitch(IfStatementSyntax ifStatement)
    {
        // Try to extract a common variable being tested
        var testVariable = ExtractTestVariable(ifStatement);
        if (testVariable == null)
            return null;

        var sections = new List<SwitchSectionSyntax>();

        // Process the if-else-if chain
        var current = ifStatement;
        while (current != null)
        {
            var condition = current.Condition;
            var caseLabel = CreateCaseLabel(condition, testVariable);
            if (caseLabel == null)
                break;

            var statements = new List<StatementSyntax>();
            if (current.Statement is BlockSyntax block)
            {
                statements.AddRange(block.Statements);
            }
            else
            {
                statements.Add(current.Statement);
            }
            statements.Add(SyntaxFactory.BreakStatement());

            var section = SyntaxFactory.SwitchSection(
                SyntaxFactory.SingletonList<SwitchLabelSyntax>(caseLabel),
                SyntaxFactory.List(statements));

            sections.Add(section);

            // Move to the next else if
            current = current.Else?.Statement as IfStatementSyntax;
        }

        // Handle the final else clause
        var finalElse = GetFinalElseClause(ifStatement);
        if (finalElse != null)
        {
            var defaultStatements = new List<StatementSyntax>();
            if (finalElse is BlockSyntax defaultBlock)
            {
                defaultStatements.AddRange(defaultBlock.Statements);
            }
            else
            {
                defaultStatements.Add(finalElse);
            }
            defaultStatements.Add(SyntaxFactory.BreakStatement());

            var defaultSection = SyntaxFactory.SwitchSection(
                SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.DefaultSwitchLabel()),
                SyntaxFactory.List(defaultStatements));

            sections.Add(defaultSection);
        }

        if (sections.Count == 0)
            return null;

        var switchStatement = SyntaxFactory.SwitchStatement(testVariable)
            .WithSections(SyntaxFactory.List(sections))
            .WithLeadingTrivia(ifStatement.GetLeadingTrivia())
            .WithTrailingTrivia(ifStatement.GetTrailingTrivia());

        return switchStatement;
    }

    private IfStatementSyntax ConvertSwitchToIf(SwitchStatementSyntax switchStatement)
    {
        var testExpression = switchStatement.Expression;
        IfStatementSyntax? rootIf = null;
        IfStatementSyntax? currentIf = null;

        foreach (var section in switchStatement.Sections)
        {
            var label = section.Labels.FirstOrDefault();
            if (label == null)
                continue;

            ExpressionSyntax? condition = null;

            if (label is CaseSwitchLabelSyntax caseLabel)
            {
                condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    testExpression,
                    caseLabel.Value);
            }
            else if (label is DefaultSwitchLabelSyntax)
            {
                // Handle default case as the final else
                var defaultStatements = section.Statements.Where(s => !s.IsKind(SyntaxKind.BreakStatement)).ToList();
                var elseStatement = defaultStatements.Count == 1 ? defaultStatements[0] : SyntaxFactory.Block(defaultStatements);

                if (currentIf != null)
                {
                    currentIf = currentIf.WithElse(SyntaxFactory.ElseClause(elseStatement));
                }
                continue;
            }

            if (condition == null)
                continue;

            var sectionStatements = section.Statements.Where(s => !s.IsKind(SyntaxKind.BreakStatement)).ToList();
            var ifStatement = sectionStatements.Count == 1 ? sectionStatements[0] : SyntaxFactory.Block(sectionStatements);

            var newIf = SyntaxFactory.IfStatement(condition, ifStatement);

            if (rootIf == null)
            {
                rootIf = newIf;
                currentIf = newIf;
            }
            else
            {
                currentIf = currentIf!.WithElse(SyntaxFactory.ElseClause(newIf));
                currentIf = newIf;
            }
        }

        return rootIf ?? SyntaxFactory.IfStatement(
            SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression),
            SyntaxFactory.Block());
    }

    private ExpressionSyntax? ExtractTestVariable(IfStatementSyntax ifStatement)
    {
        // Simple heuristic: look for binary expressions comparing a variable to a constant
        if (ifStatement.Condition is BinaryExpressionSyntax binaryExp &&
            binaryExp.IsKind(SyntaxKind.EqualsExpression))
        {
            if (binaryExp.Left is IdentifierNameSyntax || binaryExp.Left is MemberAccessExpressionSyntax)
            {
                return binaryExp.Left;
            }
        }

        return null;
    }

    private CaseSwitchLabelSyntax? CreateCaseLabel(ExpressionSyntax condition, ExpressionSyntax testVariable)
    {
        if (condition is BinaryExpressionSyntax binaryExp &&
            binaryExp.IsKind(SyntaxKind.EqualsExpression))
        {
            if (binaryExp.Left.IsEquivalentTo(testVariable))
            {
                return SyntaxFactory.CaseSwitchLabel(binaryExp.Right);
            }
            else if (binaryExp.Right.IsEquivalentTo(testVariable))
            {
                return SyntaxFactory.CaseSwitchLabel(binaryExp.Left);
            }
        }

        return null;
    }

    private StatementSyntax? GetFinalElseClause(IfStatementSyntax ifStatement)
    {
        var current = ifStatement;
        while (current != null)
        {
            if (current.Else?.Statement is IfStatementSyntax elseIfStatement)
            {
                current = elseIfStatement;
            }
            else if (current.Else?.Statement != null)
            {
                return current.Else.Statement;
            }
            else
            {
                break;
            }
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

        if (settings.ColumnNumber < 0)
        {
            throw new ArgumentException("Column number must be 0 or greater");
        }

        if (settings.Target.ToLower() != "switch" && settings.Target.ToLower() != "if")
        {
            throw new ArgumentException("Target must be either 'switch' or 'if'");
        }
    }
}