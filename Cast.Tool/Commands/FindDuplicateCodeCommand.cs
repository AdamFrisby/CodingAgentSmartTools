using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class FindDuplicateCodeCommand : BaseAnalysisCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, semanticModel) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var root = await tree.GetRootAsync();
            var sourceText = await tree.GetTextAsync();
            
            var duplicates = new List<(int line1, int line2, string content1, string content2, double similarity)>();
            
            // Find potential duplicate code blocks
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            var statements = root.DescendantNodes().OfType<StatementSyntax>().ToList();
            
            // Compare methods for similarity
            for (int i = 0; i < methods.Count; i++)
            {
                for (int j = i + 1; j < methods.Count; j++)
                {
                    var similarity = CalculateMethodSimilarity(methods[i], methods[j]);
                    if (similarity > 0.7) // 70% similarity threshold
                    {
                        var location1 = methods[i].GetLocation();
                        var location2 = methods[j].GetLocation();
                        
                        if (location1.IsInSource && location2.IsInSource)
                        {
                            var line1 = location1.GetLineSpan().StartLinePosition.Line + 1;
                            var line2 = location2.GetLineSpan().StartLinePosition.Line + 1;
                            
                            var content1 = sourceText.Lines[line1 - 1].ToString();
                            var content2 = sourceText.Lines[line2 - 1].ToString();
                            
                            duplicates.Add((line1, line2, content1, content2, similarity));
                        }
                    }
                }
            }
            
            // Compare statement blocks for similarity
            var statementBlocks = GetStatementBlocks(statements, 3); // Minimum 3 statements
            
            for (int i = 0; i < statementBlocks.Count; i++)
            {
                for (int j = i + 1; j < statementBlocks.Count; j++)
                {
                    var similarity = CalculateBlockSimilarity(statementBlocks[i], statementBlocks[j]);
                    if (similarity > 0.8) // 80% similarity threshold for statement blocks
                    {
                        var firstStmt1 = statementBlocks[i].First();
                        var firstStmt2 = statementBlocks[j].First();
                        
                        var location1 = firstStmt1.GetLocation();
                        var location2 = firstStmt2.GetLocation();
                        
                        if (location1.IsInSource && location2.IsInSource)
                        {
                            var line1 = location1.GetLineSpan().StartLinePosition.Line + 1;
                            var line2 = location2.GetLineSpan().StartLinePosition.Line + 1;
                            
                            var content1 = sourceText.Lines[line1 - 1].ToString();
                            var content2 = sourceText.Lines[line2 - 1].ToString();
                            
                            duplicates.Add((line1, line2, content1, content2, similarity));
                        }
                    }
                }
            }
            
            // Output results
            var sortedDuplicates = duplicates.OrderBy(d => d.line1).ThenBy(d => d.line2).ToList();
            
            foreach (var (line1, line2, content1, content2, similarity) in sortedDuplicates)
            {
                Console.WriteLine($"[DUPLICATE] Similarity: {similarity:P1}");
                OutputResult(settings.FilePath, line1, content1);
                OutputResult(settings.FilePath, line2, content2);
                Console.WriteLine();
            }
            
            if (!sortedDuplicates.Any())
            {
                AnsiConsole.WriteLine("[yellow]No duplicate code found[/]");
            }
            else
            {
                AnsiConsole.WriteLine($"[green]Found {sortedDuplicates.Count} potential duplicate code blocks[/]");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private static double CalculateMethodSimilarity(MethodDeclarationSyntax method1, MethodDeclarationSyntax method2)
    {
        // Simple similarity calculation based on structure and tokens
        var tokens1 = GetNormalizedTokens(method1);
        var tokens2 = GetNormalizedTokens(method2);
        
        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 1.0;
        
        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;
        
        // Calculate Jaccard similarity
        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();
        
        return (double)intersection / union;
    }
    
    private static double CalculateBlockSimilarity(List<StatementSyntax> block1, List<StatementSyntax> block2)
    {
        if (block1.Count != block2.Count)
            return 0.0;
        
        double totalSimilarity = 0.0;
        
        for (int i = 0; i < block1.Count; i++)
        {
            var tokens1 = GetNormalizedTokens(block1[i]);
            var tokens2 = GetNormalizedTokens(block2[i]);
            
            if (tokens1.Count == 0 && tokens2.Count == 0)
            {
                totalSimilarity += 1.0;
                continue;
            }
            
            if (tokens1.Count == 0 || tokens2.Count == 0)
                continue;
            
            var intersection = tokens1.Intersect(tokens2).Count();
            var union = tokens1.Union(tokens2).Count();
            
            totalSimilarity += (double)intersection / union;
        }
        
        return totalSimilarity / block1.Count;
    }
    
    private static List<string> GetNormalizedTokens(SyntaxNode node)
    {
        var tokens = new List<string>();
        
        // Get all tokens and normalize them (ignore literals, identifiers become generic)
        foreach (var token in node.DescendantTokens())
        {
            var normalizedToken = NormalizeToken(token);
            if (!string.IsNullOrWhiteSpace(normalizedToken))
            {
                tokens.Add(normalizedToken);
            }
        }
        
        return tokens;
    }
    
    private static string NormalizeToken(SyntaxToken token)
    {
        return token.Kind() switch
        {
            // Normalize identifiers to generic placeholders
            SyntaxKind.IdentifierToken => "IDENTIFIER",
            
            // Keep important structural tokens
            SyntaxKind.OpenBraceToken => "{",
            SyntaxKind.CloseBraceToken => "}",
            SyntaxKind.OpenParenToken => "(",
            SyntaxKind.CloseParenToken => ")",
            SyntaxKind.SemicolonToken => ";",
            
            // Keep keywords
            SyntaxKind.IfKeyword => "if",
            SyntaxKind.ElseKeyword => "else",
            SyntaxKind.ForKeyword => "for",
            SyntaxKind.WhileKeyword => "while",
            SyntaxKind.ReturnKeyword => "return",
            SyntaxKind.NewKeyword => "new",
            SyntaxKind.ThisKeyword => "this",
            SyntaxKind.BaseKeyword => "base",
            SyntaxKind.PublicKeyword => "public",
            SyntaxKind.PrivateKeyword => "private",
            SyntaxKind.ProtectedKeyword => "protected",
            SyntaxKind.InternalKeyword => "internal",
            SyntaxKind.StaticKeyword => "static",
            SyntaxKind.VoidKeyword => "void",
            SyntaxKind.VarKeyword => "var",
            
            // Keep operators
            SyntaxKind.PlusToken => "+",
            SyntaxKind.MinusToken => "-",
            SyntaxKind.AsteriskToken => "*",
            SyntaxKind.SlashToken => "/",
            SyntaxKind.EqualsToken => "=",
            SyntaxKind.EqualsEqualsToken => "==",
            SyntaxKind.ExclamationEqualsToken => "!=",
            SyntaxKind.LessThanToken => "<",
            SyntaxKind.GreaterThanToken => ">",
            SyntaxKind.LessThanEqualsToken => "<=",
            SyntaxKind.GreaterThanEqualsToken => ">=",
            
            // Normalize literals
            SyntaxKind.StringLiteralToken => "STRING_LITERAL",
            SyntaxKind.NumericLiteralToken => "NUMERIC_LITERAL",
            SyntaxKind.TrueKeyword => "true",
            SyntaxKind.FalseKeyword => "false",
            SyntaxKind.NullKeyword => "null",
            
            // Ignore whitespace and comments
            SyntaxKind.WhitespaceTrivia => "",
            SyntaxKind.EndOfLineTrivia => "",
            SyntaxKind.SingleLineCommentTrivia => "",
            SyntaxKind.MultiLineCommentTrivia => "",
            
            // Keep other tokens as-is
            _ => token.ValueText
        };
    }
    
    private static List<List<StatementSyntax>> GetStatementBlocks(List<StatementSyntax> statements, int minBlockSize)
    {
        var blocks = new List<List<StatementSyntax>>();
        
        // Group consecutive statements into blocks
        for (int i = 0; i <= statements.Count - minBlockSize; i++)
        {
            var block = new List<StatementSyntax>();
            
            for (int j = i; j < Math.Min(i + minBlockSize, statements.Count); j++)
            {
                block.Add(statements[j]);
            }
            
            if (block.Count >= minBlockSize)
            {
                blocks.Add(block);
            }
        }
        
        return blocks;
    }
}