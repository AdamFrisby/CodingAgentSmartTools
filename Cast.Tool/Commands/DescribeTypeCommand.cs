using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class DescribeTypeCommand : BaseAnalysisCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, semanticModel) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var root = await tree.GetRootAsync();
            
            // Find the type to describe
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
            
            // Output type description
            AnsiConsole.WriteLine($"[green]Type:[/] {targetType.ToDisplayString()}");
            AnsiConsole.WriteLine($"[blue]Kind:[/] {targetType.TypeKind}");
            
            if (targetType.BaseType != null && targetType.BaseType.SpecialType != SpecialType.System_Object)
            {
                AnsiConsole.WriteLine($"[blue]Base Type:[/] {targetType.BaseType.ToDisplayString()}");
            }
            
            if (targetType.Interfaces.Any())
            {
                AnsiConsole.WriteLine($"[blue]Interfaces:[/] {string.Join(", ", targetType.Interfaces.Select(i => i.ToDisplayString()))}");
            }
            
            // Group and display members
            var members = targetType.GetMembers().Where(m => !m.IsImplicitlyDeclared).ToList();
            
            DisplayMemberGroup("Fields", members.OfType<IFieldSymbol>());
            DisplayMemberGroup("Properties", members.OfType<IPropertySymbol>());
            DisplayMemberGroup("Methods", members.OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary));
            DisplayMemberGroup("Constructors", members.OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Constructor));
            DisplayMemberGroup("Events", members.OfType<IEventSymbol>());
            DisplayMemberGroup("Nested Types", members.OfType<INamedTypeSymbol>());
            
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
    
    private void DisplayMemberGroup<T>(string groupName, IEnumerable<T> members) where T : ISymbol
    {
        var memberList = members.ToList();
        if (!memberList.Any()) return;
        
        AnsiConsole.WriteLine($"\n[yellow]{groupName}:[/]");
        
        foreach (var member in memberList.OrderBy(m => m.Name))
        {
            var accessibility = member.DeclaredAccessibility.ToString().ToLower();
            var memberInfo = GetMemberDisplayString(member);
            AnsiConsole.WriteLine($"  [dim]{accessibility}[/] {memberInfo}");
        }
    }
    
    private string GetMemberDisplayString(ISymbol member)
    {
        return member switch
        {
            IFieldSymbol field => $"{field.Type.ToDisplayString()} {field.Name}",
            IPropertySymbol prop => $"{prop.Type.ToDisplayString()} {prop.Name} {{ {GetPropertyAccessors(prop)} }}",
            IMethodSymbol method => $"{method.ReturnType.ToDisplayString()} {method.Name}({string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})",
            IEventSymbol evt => $"event {evt.Type.ToDisplayString()} {evt.Name}",
            INamedTypeSymbol type => $"{type.TypeKind.ToString().ToLower()} {type.Name}",
            _ => member.ToDisplayString()
        };
    }
    
    private string GetPropertyAccessors(IPropertySymbol property)
    {
        var accessors = new List<string>();
        
        if (property.GetMethod != null)
        {
            var getAccessibility = property.GetMethod.DeclaredAccessibility == property.DeclaredAccessibility 
                ? "get" 
                : $"{property.GetMethod.DeclaredAccessibility.ToString().ToLower()} get";
            accessors.Add(getAccessibility);
        }
        
        if (property.SetMethod != null)
        {
            var setAccessibility = property.SetMethod.DeclaredAccessibility == property.DeclaredAccessibility 
                ? "set" 
                : $"{property.SetMethod.DeclaredAccessibility.ToString().ToLower()} set";
            accessors.Add(setAccessibility);
        }
        
        return string.Join("; ", accessors);
    }
}