using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class WhatTypeCommand : Command<WhatTypeCommand.WhatTypeSettings>
{
    public class WhatTypeSettings : BaseAnalysisCommand.Settings
    {
        [CommandOption("-d|--describe")]
        [Description("Also describe the type and its members")]
        [DefaultValue(false)]
        public bool DescribeType { get; init; } = false;
    }

    public override int Execute(CommandContext context, WhatTypeSettings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    private void ValidateInputs(WhatTypeSettings settings)
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

    public async Task<int> ExecuteAsync(CommandContext context, WhatTypeSettings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, semanticModel) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var root = await tree.GetRootAsync();
            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, settings.ColumnNumber);
            var node = root.FindNode(position);
            
            // Try to find the symbol at the specified position
            ISymbol? symbol = null;
            ITypeSymbol? typeSymbol = null;
            
            // Check various node types to find symbol information
            if (node is IdentifierNameSyntax identifier)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                symbol = symbolInfo.Symbol;
                
                if (symbol != null)
                {
                    typeSymbol = GetSymbolType(symbol);
                }
                else
                {
                    // Try to get type information directly
                    var typeInfo = semanticModel.GetTypeInfo(identifier);
                    typeSymbol = typeInfo.Type;
                }
            }
            else if (node is VariableDeclaratorSyntax variableDeclarator)
            {
                symbol = semanticModel.GetDeclaredSymbol(variableDeclarator);
                if (symbol != null)
                {
                    typeSymbol = GetSymbolType(symbol);
                }
            }
            else if (node is ParameterSyntax parameter)
            {
                symbol = semanticModel.GetDeclaredSymbol(parameter);
                if (symbol != null)
                {
                    typeSymbol = GetSymbolType(symbol);
                }
            }
            else if (node is PropertyDeclarationSyntax property)
            {
                symbol = semanticModel.GetDeclaredSymbol(property);
                if (symbol != null)
                {
                    typeSymbol = GetSymbolType(symbol);
                }
            }
            else if (node is FieldDeclarationSyntax field)
            {
                // For fields, we need to get the variable declarator
                var declarator = field.Declaration.Variables.FirstOrDefault();
                if (declarator != null)
                {
                    symbol = semanticModel.GetDeclaredSymbol(declarator);
                    if (symbol != null)
                    {
                        typeSymbol = GetSymbolType(symbol);
                    }
                }
            }
            else if (node is MethodDeclarationSyntax method)
            {
                symbol = semanticModel.GetDeclaredSymbol(method);
                if (symbol is IMethodSymbol methodSymbol)
                {
                    typeSymbol = methodSymbol.ReturnType;
                }
            }
            else
            {
                // Try to get type information from any expression
                var typeInfo = semanticModel.GetTypeInfo(node);
                typeSymbol = typeInfo.Type;
                
                // Also try to get symbol info
                var symbolInfo = semanticModel.GetSymbolInfo(node);
                symbol = symbolInfo.Symbol;
            }
            
            if (typeSymbol == null && symbol == null)
            {
                AnsiConsole.WriteLine($"[red]Error: No type information found at line {settings.LineNumber}, column {settings.ColumnNumber}.[/]");
                return 1;
            }
            
            // Output symbol information if available
            if (symbol != null)
            {
                AnsiConsole.WriteLine($"[green]Symbol:[/] {symbol.Name}");
                AnsiConsole.WriteLine($"[blue]Symbol Kind:[/] {symbol.Kind}");
            }
            
            // Output type information
            if (typeSymbol != null)
            {
                AnsiConsole.WriteLine($"[green]Type:[/] {typeSymbol.ToDisplayString()}");
                AnsiConsole.WriteLine($"[blue]Type Kind:[/] {typeSymbol.TypeKind}");
                
                if (typeSymbol.ContainingNamespace != null && !typeSymbol.ContainingNamespace.IsGlobalNamespace)
                {
                    AnsiConsole.WriteLine($"[blue]Namespace:[/] {typeSymbol.ContainingNamespace.ToDisplayString()}");
                }
                
                if (typeSymbol.ContainingAssembly != null)
                {
                    AnsiConsole.WriteLine($"[blue]Assembly:[/] {typeSymbol.ContainingAssembly.Name}");
                }
                
                // If describe flag is set and we have a named type, describe it
                if (settings.DescribeType && typeSymbol is INamedTypeSymbol namedType)
                {
                    AnsiConsole.WriteLine("\n[yellow]Type Description:[/]");
                    DescribeType(namedType);
                }
            }
            else if (symbol != null)
            {
                AnsiConsole.WriteLine($"[yellow]Warning: Could not determine type for symbol '{symbol.Name}'[/]");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private ITypeSymbol? GetSymbolType(ISymbol symbol)
    {
        return symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IParameterSymbol parameter => parameter.Type,
            ILocalSymbol local => local.Type,
            IMethodSymbol method => method.ReturnType,
            IEventSymbol eventSymbol => eventSymbol.Type,
            INamedTypeSymbol type => type,
            _ => null
        };
    }
    
    private void DescribeType(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
        {
            AnsiConsole.WriteLine($"[blue]Base Type:[/] {typeSymbol.BaseType.ToDisplayString()}");
        }
        
        if (typeSymbol.Interfaces.Any())
        {
            AnsiConsole.WriteLine($"[blue]Interfaces:[/] {string.Join(", ", typeSymbol.Interfaces.Select(i => i.ToDisplayString()))}");
        }
        
        // Show member count summary
        var members = typeSymbol.GetMembers().Where(m => !m.IsImplicitlyDeclared).ToList();
        var memberCounts = members.GroupBy(m => m.Kind).ToDictionary(g => g.Key, g => g.Count());
        
        if (memberCounts.Any())
        {
            var countStrings = memberCounts.Select(kvp => $"{kvp.Value} {kvp.Key.ToString().ToLower()}{(kvp.Value == 1 ? "" : "s")}");
            AnsiConsole.WriteLine($"[blue]Members:[/] {string.Join(", ", countStrings)}");
        }
    }
}

// No need for wrapper class since we're now inheriting directly from Command