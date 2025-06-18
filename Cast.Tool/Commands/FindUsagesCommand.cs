using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class FindUsagesCommand : BaseAnalysisCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, semanticModel) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var root = await tree.GetRootAsync();
            var sourceText = await tree.GetTextAsync();
            
            // Find the symbol at the specified position
            var node = root.FindNode(position);
            var symbolInfo = semanticModel.GetSymbolInfo(node);
            
            if (symbolInfo.Symbol == null)
            {
                AnsiConsole.WriteLine("[red]Error: No symbol found at the specified location[/]");
                return 1;
            }
            
            var targetSymbol = symbolInfo.Symbol;
            var foundUsages = new List<(int line, string content, string usageType)>();
            
            // Find all usages (excluding declarations) of this symbol in the current file
            var allNodes = root.DescendantNodes();
            
            foreach (var usageNode in allNodes)
            {
                try
                {
                    var usageSymbolInfo = semanticModel.GetSymbolInfo(usageNode);
                    if (usageSymbolInfo.Symbol != null && SymbolEqualityComparer.Default.Equals(usageSymbolInfo.Symbol, targetSymbol))
                    {
                        // Skip declarations - we only want actual usages
                        if (IsDeclaration(usageNode))
                            continue;
                            
                        var location = usageNode.GetLocation();
                        if (location.IsInSource)
                        {
                            var lineSpan = location.GetLineSpan();
                            var lineNumber = lineSpan.StartLinePosition.Line + 1;
                            var line = sourceText.Lines[lineNumber - 1];
                            var usageType = GetUsageType(usageNode);
                            foundUsages.Add((lineNumber, line.ToString(), usageType));
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue with other nodes if semantic analysis fails for one
                }
            }
            
            // Remove duplicates and sort by line number
            var uniqueUsages = foundUsages
                .GroupBy(u => u.line)
                .Select(g => g.First())
                .OrderBy(u => u.line);
            
            foreach (var (line, content, usageType) in uniqueUsages)
            {
                OutputResult(settings.FilePath, line, $"{content} // Usage: {usageType}");
            }
            
            if (!uniqueUsages.Any())
            {
                AnsiConsole.WriteLine($"[yellow]No usages found for symbol '{targetSymbol.Name}' at line {settings.LineNumber}, column {settings.ColumnNumber}[/]");
            }
            else
            {
                AnsiConsole.WriteLine($"[green]Found {uniqueUsages.Count()} usages of symbol '{targetSymbol.Name}'[/]");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private static bool IsDeclaration(SyntaxNode node)
    {
        // Check if this node is part of a declaration rather than a usage
        return node.AncestorsAndSelf().Any(ancestor => 
            ancestor is VariableDeclaratorSyntax or
            MethodDeclarationSyntax or
            PropertyDeclarationSyntax or
            FieldDeclarationSyntax or
            ClassDeclarationSyntax or
            InterfaceDeclarationSyntax or
            StructDeclarationSyntax or
            EnumDeclarationSyntax or
            ParameterSyntax) &&
            node.Parent is not InvocationExpressionSyntax &&
            node.Parent is not MemberAccessExpressionSyntax &&
            node.Parent is not AssignmentExpressionSyntax;
    }
    
    private static string GetUsageType(SyntaxNode node)
    {
        // Determine the type of usage
        var parent = node.Parent;
        
        return parent switch
        {
            InvocationExpressionSyntax => "Method Call",
            MemberAccessExpressionSyntax => "Member Access",
            AssignmentExpressionSyntax assignment when assignment.Left == node => "Assignment Target",
            AssignmentExpressionSyntax => "Assignment Value",
            BinaryExpressionSyntax => "Binary Expression",
            ReturnStatementSyntax => "Return Value",
            ArgumentSyntax => "Method Argument",
            VariableDeclarationSyntax => "Variable Declaration",
            IfStatementSyntax => "Condition",
            WhileStatementSyntax => "Loop Condition",
            ForStatementSyntax => "For Loop",
            _ => "General Usage"
        };
    }
}