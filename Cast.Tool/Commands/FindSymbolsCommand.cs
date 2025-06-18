using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class FindSymbolsCommand : BaseAnalysisCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            ValidateInputs(settings);
            
            if (string.IsNullOrWhiteSpace(settings.Pattern))
            {
                AnsiConsole.WriteLine("[red]Error: Pattern is required for symbol search. Use --pattern option.[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var (document, tree, semanticModel) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var root = await tree.GetRootAsync();
            var sourceText = await tree.GetTextAsync();
            
            var foundSymbols = new List<(SyntaxNode node, int line, string content)>();
            
            // Find symbol declarations that match the pattern
            var symbolNodes = root.DescendantNodes().Where(node => 
                IsSymbolDeclaration(node) && MatchesPattern(GetSymbolName(node), settings.Pattern));
            
            foreach (var node in symbolNodes)
            {
                var location = node.GetLocation();
                if (location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    var lineNumber = lineSpan.StartLinePosition.Line + 1; // Convert to 1-based
                    var line = sourceText.Lines[lineNumber - 1];
                    foundSymbols.Add((node, lineNumber, line.ToString()));
                }
            }
            
            // Also search in semantic model for additional symbols
            await FindSymbolsInSemanticModel(semanticModel, tree, settings.Pattern, foundSymbols);
            
            // Sort by line number and output results
            foreach (var (node, line, content) in foundSymbols.OrderBy(x => x.line))
            {
                OutputResult(settings.FilePath, line, content);
            }
            
            if (!foundSymbols.Any())
            {
                AnsiConsole.WriteLine($"[yellow]No symbols found matching pattern '{settings.Pattern}'[/]");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private static bool IsSymbolDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax or
               InterfaceDeclarationSyntax or
               StructDeclarationSyntax or
               EnumDeclarationSyntax or
               MethodDeclarationSyntax or
               PropertyDeclarationSyntax or
               FieldDeclarationSyntax or
               EventDeclarationSyntax or
               DelegateDeclarationSyntax or
               VariableDeclaratorSyntax or
               ParameterSyntax;
    }
    
    private static string GetSymbolName(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax cls => cls.Identifier.ValueText,
            InterfaceDeclarationSyntax iface => iface.Identifier.ValueText,
            StructDeclarationSyntax str => str.Identifier.ValueText,
            EnumDeclarationSyntax enm => enm.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax prop => prop.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "",
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            DelegateDeclarationSyntax del => del.Identifier.ValueText,
            VariableDeclaratorSyntax var => var.Identifier.ValueText,
            ParameterSyntax param => param.Identifier.ValueText,
            _ => ""
        };
    }
    
    private static bool MatchesPattern(string symbolName, string pattern)
    {
        if (string.IsNullOrEmpty(symbolName) || string.IsNullOrEmpty(pattern))
            return false;
            
        // Support simple wildcard patterns and partial matches
        if (pattern.Contains('*'))
        {
            var regexPattern = "^" + pattern.Replace("*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(symbolName, regexPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        // Partial match (case-insensitive)
        return symbolName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
    
    private async Task FindSymbolsInSemanticModel(SemanticModel semanticModel, SyntaxTree tree, string pattern, 
        List<(SyntaxNode node, int line, string content)> foundSymbols)
    {
        try
        {
            var root = await tree.GetRootAsync();
            var sourceText = await tree.GetTextAsync();
            
            // Find identifiers that match the pattern but weren't caught by syntax analysis
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => MatchesPattern(id.Identifier.ValueText, pattern))
                .GroupBy(id => id.Identifier.ValueText)
                .Select(g => g.First()); // Take first occurrence of each unique identifier
            
            foreach (var identifier in identifiers)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                if (symbolInfo.Symbol != null)
                {
                    var location = identifier.GetLocation();
                    if (location.IsInSource)
                    {
                        var lineSpan = location.GetLineSpan();
                        var lineNumber = lineSpan.StartLinePosition.Line + 1;
                        var line = sourceText.Lines[lineNumber - 1];
                        
                        // Avoid duplicates
                        if (!foundSymbols.Any(fs => fs.line == lineNumber && fs.content == line.ToString()))
                        {
                            foundSymbols.Add((identifier, lineNumber, line.ToString()));
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore semantic model errors and continue with syntax-only analysis
        }
    }
}