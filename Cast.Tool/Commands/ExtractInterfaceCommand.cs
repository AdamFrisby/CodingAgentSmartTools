using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class ExtractInterfaceCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--class-name")]
        [Description("Name of the class to extract interface from")]
        public string ClassName { get; set; } = string.Empty;

        [CommandOption("--interface-name")]
        [Description("Name for the new interface")]
        public string InterfaceName { get; set; } = string.Empty;

        [CommandOption("--members")]
        [Description("Comma-separated list of member names to include in interface (defaults to all public members)")]
        public string Members { get; set; } = string.Empty;

        [CommandOption("--output")]
        [Description("Output file path (default: modify in place)")]
        public string? OutputPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class ExtractInterfaceCommand : Command<ExtractInterfaceCommandSettings>
    {
        public override int Execute(CommandContext context, ExtractInterfaceCommandSettings settings)
        {
            try
            {
                if (!File.Exists(settings.FilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.ClassName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Class name is required[/]");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.InterfaceName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Interface name is required[/]");
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var targetClass = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.ValueText == settings.ClassName);

                if (targetClass == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: Class '{0}' not found[/]", settings.ClassName);
                    return 1;
                }

                var membersToExtract = new List<string>();
                if (!string.IsNullOrWhiteSpace(settings.Members))
                {
                    membersToExtract = settings.Members.Split(',')
                        .Select(m => m.Trim())
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();
                }

                var (modifiedRoot, interfaceCode) = ExtractInterface(root, targetClass, settings.InterfaceName, membersToExtract);

                if (settings.DryRun)
                {
                    AnsiConsole.MarkupLine("[green]Would extract interface '{0}' from class '{1}' in {2}[/]", 
                        settings.InterfaceName, settings.ClassName, settings.FilePath);
                    
                    if (membersToExtract.Any())
                    {
                        AnsiConsole.MarkupLine("[yellow]Members to extract: {0}[/]", string.Join(", ", membersToExtract));
                    }
                    
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Generated interface:[/]");
                    AnsiConsole.WriteLine(interfaceCode);
                    return 0;
                }

                var outputPath = settings.OutputPath ?? settings.FilePath;
                var modifiedCode = modifiedRoot.ToFullString();
                File.WriteAllText(outputPath, modifiedCode);

                // Write interface to a separate file
                var interfacePath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"{settings.InterfaceName}.cs");
                File.WriteAllText(interfacePath, interfaceCode);

                AnsiConsole.MarkupLine("[green]Successfully extracted interface '{0}' from '{1}' in {2}[/]", 
                    settings.InterfaceName, settings.ClassName, outputPath);
                AnsiConsole.MarkupLine("[green]Interface written to: {0}[/]", interfacePath);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static (CompilationUnitSyntax modifiedRoot, string interfaceCode) ExtractInterface(
            CompilationUnitSyntax root,
            ClassDeclarationSyntax targetClass,
            string interfaceName,
            List<string> membersToExtract)
        {
            var interfaceMembers = new List<MemberDeclarationSyntax>();

            foreach (var member in targetClass.Members)
            {
                if (!IsPublicMember(member))
                    continue;

                var memberName = GetMemberName(member);
                if (membersToExtract.Any() && !membersToExtract.Contains(memberName))
                    continue;

                var interfaceMember = ConvertToInterfaceMember(member);
                if (interfaceMember != null)
                {
                    interfaceMembers.Add(interfaceMember);
                }
            }

            // Create interface
            var interfaceDecl = SyntaxFactory.InterfaceDeclaration(interfaceName)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithMembers(SyntaxFactory.List(interfaceMembers));

            // Create namespace for interface if original class is in a namespace
            var namespaceDeclaration = targetClass.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
            var interfaceRoot = SyntaxFactory.CompilationUnit();

            // Copy using directives
            interfaceRoot = interfaceRoot.WithUsings(root.Usings);

            if (namespaceDeclaration != null)
            {
                var interfaceNamespace = SyntaxFactory.NamespaceDeclaration(namespaceDeclaration.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
                interfaceRoot = interfaceRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceNamespace));
            }
            else
            {
                interfaceRoot = interfaceRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(interfaceDecl));
            }

            // Modify original class to implement the interface
            var interfaceType = SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName(interfaceName));
            var baseList = targetClass.BaseList ?? SyntaxFactory.BaseList();
            baseList = baseList.WithTypes(baseList.Types.Add(interfaceType));

            var modifiedClass = targetClass.WithBaseList(baseList);
            var modifiedRoot = root.ReplaceNode(targetClass, modifiedClass);

            return (modifiedRoot, interfaceRoot.NormalizeWhitespace().ToFullString());
        }

        private static bool IsPublicMember(MemberDeclarationSyntax member)
        {
            return member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
        }

        private static string GetMemberName(MemberDeclarationSyntax member)
        {
            return member switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                EventDeclarationSyntax eventDecl => eventDecl.Identifier.ValueText,
                _ => ""
            };
        }

        private static MemberDeclarationSyntax? ConvertToInterfaceMember(MemberDeclarationSyntax member)
        {
            return member switch
            {
                MethodDeclarationSyntax method => ConvertMethodToInterfaceMethod(method),
                PropertyDeclarationSyntax property => ConvertPropertyToInterfaceProperty(property),
                EventDeclarationSyntax eventDecl => ConvertEventToInterfaceEvent(eventDecl),
                _ => null
            };
        }

        private static MethodDeclarationSyntax? ConvertMethodToInterfaceMethod(MethodDeclarationSyntax method)
        {
            // Skip static methods, constructors, and methods with bodies
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) ||
                method.Identifier.ValueText.StartsWith("~"))
                return null;

            return SyntaxFactory.MethodDeclaration(method.ReturnType, method.Identifier)
                .WithParameterList(method.ParameterList)
                .WithTypeParameterList(method.TypeParameterList)
                .WithConstraintClauses(method.ConstraintClauses)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static PropertyDeclarationSyntax? ConvertPropertyToInterfaceProperty(PropertyDeclarationSyntax property)
        {
            // Skip static properties
            if (property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                return null;

            var accessors = new List<AccessorDeclarationSyntax>();

            if (property.AccessorList != null)
            {
                foreach (var accessor in property.AccessorList.Accessors)
                {
                    if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration) || 
                        accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                    {
                        accessors.Add(SyntaxFactory.AccessorDeclaration(accessor.Kind())
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                    }
                }
            }

            return SyntaxFactory.PropertyDeclaration(property.Type, property.Identifier)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        private static EventDeclarationSyntax? ConvertEventToInterfaceEvent(EventDeclarationSyntax eventDecl)
        {
            // Skip static events
            if (eventDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                return null;

            return SyntaxFactory.EventDeclaration(eventDecl.Type, eventDecl.Identifier)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }
    }
}