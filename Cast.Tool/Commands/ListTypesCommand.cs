using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class ListTypesCommand : Command<ListTypesCommand.ListTypesSettings>
{
    public class ListTypesSettings : BaseAnalysisCommand.Settings
    {
        [CommandOption("-a|--assembly")]
        [Description("Filter by assembly name (partial match)")]
        public string? AssemblyFilter { get; init; }
        
        [CommandOption("--include-system")]
        [Description("Include system types (default: false)")]
        [DefaultValue(false)]
        public bool IncludeSystemTypes { get; init; } = false;
        
        [CommandOption("--local-only")]
        [Description("Show only types defined in the current file")]
        [DefaultValue(false)]
        public bool LocalOnly { get; init; } = false;
    }

    public override int Execute(CommandContext context, ListTypesSettings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    private void ValidateInputs(ListTypesSettings settings)
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

    public async Task<int> ExecuteAsync(CommandContext context, ListTypesSettings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, semanticModel) = await engine.LoadDocumentAsync(settings.FilePath);
            
            var compilation = semanticModel.Compilation;
            var allTypes = new List<(INamedTypeSymbol type, string source)>();
            
            if (settings.LocalOnly)
            {
                // Only show types defined in the current file
                var root = await tree.GetRootAsync();
                var localTypes = root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(typeDecl => semanticModel.GetDeclaredSymbol(typeDecl))
                    .OfType<INamedTypeSymbol>()
                    .Where(type => type != null);
                
                foreach (var type in localTypes)
                {
                    allTypes.Add((type, "Local"));
                }
            }
            else
            {
                // Get types from all referenced assemblies
                var assembliesToCheck = new List<(IAssemblySymbol assembly, string source)>
                {
                    (compilation.Assembly, "Current Project")
                };
                
                // Add referenced assemblies
                foreach (var reference in compilation.References)
                {
                    var assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                    if (assembly != null)
                    {
                        var isSystemAssembly = IsSystemAssembly(assembly);
                        if (settings.IncludeSystemTypes || !isSystemAssembly)
                        {
                            assembliesToCheck.Add((assembly, assembly.Name));
                        }
                    }
                }
                
                // Collect types from each assembly
                foreach (var (assembly, source) in assembliesToCheck)
                {
                    if (settings.AssemblyFilter != null && 
                        !assembly.Name.Contains(settings.AssemblyFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    var typesInAssembly = GetTypesFromAssembly(assembly);
                    foreach (var type in typesInAssembly)
                    {
                        allTypes.Add((type, source));
                    }
                }
            }
            
            // Sort types by namespace, then by name
            var sortedTypes = allTypes
                .OrderBy(t => t.type.ContainingNamespace?.ToDisplayString() ?? "")
                .ThenBy(t => t.type.Name)
                .ToList();
            
            if (!sortedTypes.Any())
            {
                AnsiConsole.WriteLine("[yellow]No types found matching the specified criteria.[/]");
                return 0;
            }
            
            // Group by namespace for better display
            var typesByNamespace = sortedTypes.GroupBy(t => t.type.ContainingNamespace?.ToDisplayString() ?? "<global>");
            
            foreach (var namespaceGroup in typesByNamespace)
            {
                AnsiConsole.WriteLine($"\n[blue]{namespaceGroup.Key}[/]");
                
                foreach (var (type, source) in namespaceGroup)
                {
                    var typeKindDisplay = GetTypeKindDisplay(type.TypeKind);
                    var sourceDisplay = settings.LocalOnly ? "" : $" [{source}]";
                    
                    AnsiConsole.WriteLine($"  [green]{typeKindDisplay}[/] {type.Name}{sourceDisplay}");
                }
            }
            
            // Show summary
            var totalCount = sortedTypes.Count;
            var sourceGroups = sortedTypes.GroupBy(t => t.source).ToDictionary(g => g.Key, g => g.Count());
            
            AnsiConsole.WriteLine($"\n[yellow]Total: {totalCount} types[/]");
            
            if (!settings.LocalOnly && sourceGroups.Count > 1)
            {
                foreach (var sourceGroup in sourceGroups.OrderBy(kvp => kvp.Key))
                {
                    AnsiConsole.WriteLine($"  {sourceGroup.Key}: {sourceGroup.Value} types");
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
    
    private bool IsSystemAssembly(IAssemblySymbol assembly)
    {
        var assemblyName = assembly.Name;
        return assemblyName.StartsWith("System") ||
               assemblyName.StartsWith("Microsoft") ||
               assemblyName.StartsWith("mscorlib") ||
               assemblyName == "netstandard";
    }
    
    private IEnumerable<INamedTypeSymbol> GetTypesFromAssembly(IAssemblySymbol assembly)
    {
        return GetTypesFromNamespace(assembly.GlobalNamespace);
    }
    
    private IEnumerable<INamedTypeSymbol> GetTypesFromNamespace(INamespaceSymbol namespaceSymbol)
    {
        // Get types directly in this namespace
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
            
            // Also get nested types
            foreach (var nestedType in GetNestedTypes(type))
            {
                yield return nestedType;
            }
        }
        
        // Recursively get types from child namespaces
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetTypesFromNamespace(childNamespace))
            {
                yield return type;
            }
        }
    }
    
    private IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nestedType in type.GetTypeMembers())
        {
            yield return nestedType;
            
            // Recursively get nested types
            foreach (var deepNestedType in GetNestedTypes(nestedType))
            {
                yield return deepNestedType;
            }
        }
    }
    
    private string GetTypeKindDisplay(TypeKind typeKind)
    {
        return typeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => typeKind.ToString().ToLower()
        };
    }
}

// No need for wrapper class since we're now inheriting directly from Command