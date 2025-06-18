using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class FindReferencesCommand : BaseAnalysisCommand
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
            var foundReferences = new List<(int line, string content)>();
            
            // Find all references to this symbol in the current file
            var allNodes = root.DescendantNodes();
            
            foreach (var refNode in allNodes)
            {
                try
                {
                    var refSymbolInfo = semanticModel.GetSymbolInfo(refNode);
                    if (refSymbolInfo.Symbol != null && SymbolEqualityComparer.Default.Equals(refSymbolInfo.Symbol, targetSymbol))
                    {
                        var location = refNode.GetLocation();
                        if (location.IsInSource)
                        {
                            var lineSpan = location.GetLineSpan();
                            var lineNumber = lineSpan.StartLinePosition.Line + 1;
                            var line = sourceText.Lines[lineNumber - 1];
                            foundReferences.Add((lineNumber, line.ToString()));
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue with other nodes if semantic analysis fails for one
                }
            }
            
            // Also check for symbol declarations
            foreach (var declNode in allNodes)
            {
                try
                {
                    var declSymbol = semanticModel.GetDeclaredSymbol(declNode);
                    if (declSymbol != null && SymbolEqualityComparer.Default.Equals(declSymbol, targetSymbol))
                    {
                        var location = declNode.GetLocation();
                        if (location.IsInSource)
                        {
                            var lineSpan = location.GetLineSpan();
                            var lineNumber = lineSpan.StartLinePosition.Line + 1;
                            var line = sourceText.Lines[lineNumber - 1];
                            foundReferences.Add((lineNumber, line.ToString()));
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue with other nodes if semantic analysis fails for one
                }
            }
            
            // Remove duplicates and sort by line number
            var uniqueReferences = foundReferences
                .GroupBy(r => r.line)
                .Select(g => g.First())
                .OrderBy(r => r.line);
            
            foreach (var (line, content) in uniqueReferences)
            {
                OutputResult(settings.FilePath, line, content);
            }
            
            if (!uniqueReferences.Any())
            {
                AnsiConsole.WriteLine($"[yellow]No references found for symbol '{targetSymbol.Name}' at line {settings.LineNumber}, column {settings.ColumnNumber}[/]");
            }
            else
            {
                AnsiConsole.WriteLine($"[green]Found {uniqueReferences.Count()} references to symbol '{targetSymbol.Name}'[/]");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}