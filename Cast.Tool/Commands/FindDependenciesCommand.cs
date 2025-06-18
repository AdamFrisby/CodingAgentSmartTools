using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class FindDependenciesCommand : BaseAnalysisCommand
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
            
            // Find the type to analyze dependencies for
            INamedTypeSymbol? targetType = null;
            
            if (!string.IsNullOrWhiteSpace(settings.TypeName))
            {
                // Find type by name
                targetType = FindTypeByName(root, semanticModel, settings.TypeName);
            }
            else
            {
                // Find type at specified position
                var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
                var node = root.FindNode(position);
                var typeDeclaration = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                
                if (typeDeclaration != null)
                {
                    targetType = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                }
            }
            
            if (targetType == null)
            {
                AnsiConsole.WriteLine("[red]Error: No type found. Use --type option or specify a position within a type declaration.[/]");
                return 1;
            }
            
            var dependencies = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            var foundDependencies = new List<(int line, string content, string dependencyType)>();
            
            // Analyze dependencies
            AnalyzeDependencies(targetType, root, semanticModel, sourceText, dependencies, foundDependencies);
            
            // Sort by line number and output results
            var sortedDependencies = foundDependencies
                .OrderBy(d => d.line)
                .ToList();
                
            foreach (var (line, content, dependencyType) in sortedDependencies)
            {
                OutputResult(settings.FilePath, line, $"{content} // Dependency: {dependencyType}");
            }
            
            if (!sortedDependencies.Any())
            {
                AnsiConsole.WriteLine($"[yellow]No dependencies found for type '{targetType.Name}'[/]");
            }
            else
            {
                AnsiConsole.WriteLine($"[green]Found {sortedDependencies.Count} dependencies for type '{targetType.Name}'[/]");
                
                // Output summary of unique dependency types
                var uniqueDependencies = dependencies.Select(d => d.ToDisplayString()).Distinct().OrderBy(x => x);
                AnsiConsole.WriteLine("[blue]Unique dependency types:[/]");
                foreach (var dep in uniqueDependencies)
                {
                    Console.WriteLine($"  - {dep}");
                }
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private INamedTypeSymbol? FindTypeByName(SyntaxNode root, SemanticModel semanticModel, string typeName)
    {
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
        
        foreach (var typeDecl in typeDeclarations)
        {
            if (typeDecl.Identifier.ValueText.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                return semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
            }
        }
        
        return null;
    }
    
    private void AnalyzeDependencies(INamedTypeSymbol targetType, SyntaxNode root, SemanticModel semanticModel, 
        Microsoft.CodeAnalysis.Text.SourceText sourceText, HashSet<ITypeSymbol> dependencies, 
        List<(int line, string content, string dependencyType)> foundDependencies)
    {
        // Find the type declaration in syntax tree
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Where(t => semanticModel.GetDeclaredSymbol(t) is INamedTypeSymbol symbol && 
                       SymbolEqualityComparer.Default.Equals(symbol, targetType));
        
        foreach (var typeDecl in typeDeclarations)
        {
            // Analyze base type
            if (typeDecl.BaseList != null)
            {
                foreach (var baseType in typeDecl.BaseList.Types)
                {
                    AnalyzeTypeReference(baseType.Type, semanticModel, sourceText, dependencies, foundDependencies, "Base Type");
                }
            }
            
            // Analyze members
            foreach (var member in typeDecl.Members)
            {
                AnalyzeMember(member, semanticModel, sourceText, dependencies, foundDependencies);
            }
        }
    }
    
    private void AnalyzeMember(MemberDeclarationSyntax member, SemanticModel semanticModel, 
        Microsoft.CodeAnalysis.Text.SourceText sourceText, HashSet<ITypeSymbol> dependencies, 
        List<(int line, string content, string dependencyType)> foundDependencies)
    {
        // Analyze field dependencies
        if (member is FieldDeclarationSyntax field)
        {
            AnalyzeTypeReference(field.Declaration.Type, semanticModel, sourceText, dependencies, foundDependencies, "Field Type");
        }
        
        // Analyze property dependencies
        if (member is PropertyDeclarationSyntax property)
        {
            AnalyzeTypeReference(property.Type, semanticModel, sourceText, dependencies, foundDependencies, "Property Type");
        }
        
        // Analyze method dependencies
        if (member is MethodDeclarationSyntax method)
        {
            // Return type
            AnalyzeTypeReference(method.ReturnType, semanticModel, sourceText, dependencies, foundDependencies, "Return Type");
            
            // Parameters
            if (method.ParameterList != null)
            {
                foreach (var param in method.ParameterList.Parameters)
                {
                    if (param.Type != null)
                    {
                        AnalyzeTypeReference(param.Type, semanticModel, sourceText, dependencies, foundDependencies, "Parameter Type");
                    }
                }
            }
            
            // Method body dependencies
            if (method.Body != null)
            {
                AnalyzeBlockDependencies(method.Body, semanticModel, sourceText, dependencies, foundDependencies);
            }
        }
    }
    
    private void AnalyzeBlockDependencies(BlockSyntax block, SemanticModel semanticModel, 
        Microsoft.CodeAnalysis.Text.SourceText sourceText, HashSet<ITypeSymbol> dependencies, 
        List<(int line, string content, string dependencyType)> foundDependencies)
    {
        foreach (var statement in block.Statements)
        {
            // Analyze variable declarations
            if (statement is LocalDeclarationStatementSyntax localDecl)
            {
                AnalyzeTypeReference(localDecl.Declaration.Type, semanticModel, sourceText, dependencies, foundDependencies, "Local Variable Type");
            }
            
            // Analyze object creation expressions
            var objectCreations = statement.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var objCreation in objectCreations)
            {
                AnalyzeTypeReference(objCreation.Type, semanticModel, sourceText, dependencies, foundDependencies, "Object Creation");
            }
            
            // Analyze method invocations
            var invocations = statement.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
                    {
                        var location = invocation.GetLocation();
                        if (location.IsInSource)
                        {
                            var lineSpan = location.GetLineSpan();
                            var lineNumber = lineSpan.StartLinePosition.Line + 1;
                            var line = sourceText.Lines[lineNumber - 1];
                            
                            dependencies.Add(methodSymbol.ContainingType);
                            foundDependencies.Add((lineNumber, line.ToString(), "Method Call"));
                        }
                    }
                }
                catch (Exception)
                {
                    // Continue with other invocations if one fails
                }
            }
        }
    }
    
    private void AnalyzeTypeReference(TypeSyntax typeSyntax, SemanticModel semanticModel, 
        Microsoft.CodeAnalysis.Text.SourceText sourceText, HashSet<ITypeSymbol> dependencies, 
        List<(int line, string content, string dependencyType)> foundDependencies, string dependencyType)
    {
        try
        {
            var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
            if (typeInfo.Type != null)
            {
                var location = typeSyntax.GetLocation();
                if (location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    var lineNumber = lineSpan.StartLinePosition.Line + 1;
                    var line = sourceText.Lines[lineNumber - 1];
                    
                    dependencies.Add(typeInfo.Type);
                    foundDependencies.Add((lineNumber, line.ToString(), dependencyType));
                }
            }
        }
        catch (Exception)
        {
            // Continue with other type references if one fails
        }
    }
}