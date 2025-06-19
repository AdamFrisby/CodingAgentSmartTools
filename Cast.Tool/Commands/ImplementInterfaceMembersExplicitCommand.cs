using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class ImplementInterfaceMembersExplicitCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--class-name")]
        [Description("Name of the class to implement interface members in")]
        public string ClassName { get; set; } = string.Empty;

        [CommandOption("--interface-name")]
        [Description("Name of the interface to implement members for")]
        public string InterfaceName { get; set; } = string.Empty;

        [CommandOption("--output")]
        [Description("Output file path (default: modify in place)")]
        public string? OutputPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class ImplementInterfaceMembersExplicitCommand : Command<ImplementInterfaceMembersExplicitCommandSettings>
    {
        public override int Execute(CommandContext context, ImplementInterfaceMembersExplicitCommandSettings settings)
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

                var modifiedRoot = ImplementInterfaceMembers(root, targetClass, settings.InterfaceName);

                if (settings.DryRun)
                {
                    AnsiConsole.MarkupLine("[green]Would implement interface '{0}' members explicitly in class '{1}' in {2}[/]", 
                        settings.InterfaceName, settings.ClassName, settings.FilePath);
                    
                    // Show diff for the changes
                    var dryRunModifiedCode = modifiedRoot.ToFullString();
                    AnsiConsole.WriteLine();
                    DiffUtility.DisplayDiff(sourceCode, dryRunModifiedCode, settings.FilePath);
                    
                    return 0;
                }

                var outputPath = settings.OutputPath ?? settings.FilePath;
                var modifiedCode = modifiedRoot.ToFullString();
                File.WriteAllText(outputPath, modifiedCode);

                AnsiConsole.MarkupLine("[green]Successfully implemented interface '{0}' members explicitly in class '{1}' in {2}[/]", 
                    settings.InterfaceName, settings.ClassName, outputPath);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static CompilationUnitSyntax ImplementInterfaceMembers(
            CompilationUnitSyntax root,
            ClassDeclarationSyntax targetClass,
            string interfaceName)
        {
            // Find the interface in the same file or create default implementations
            var interfaceDecl = root.DescendantNodes()
                .OfType<InterfaceDeclarationSyntax>()
                .FirstOrDefault(i => i.Identifier.ValueText == interfaceName);

            var membersToImplement = new List<MemberDeclarationSyntax>();

            if (interfaceDecl != null)
            {
                // Extract members from the interface declaration
                foreach (var member in interfaceDecl.Members)
                {
                    var explicitMember = CreateExplicitImplementation(member, interfaceName);
                    if (explicitMember != null)
                    {
                        membersToImplement.Add(explicitMember);
                    }
                }
            }
            else
            {
                // Create sample interface members if interface not found
                membersToImplement.AddRange(CreateSampleInterfaceImplementations(interfaceName));
            }

            // Add the explicit implementations to the class
            var newMembers = targetClass.Members.AddRange(membersToImplement);
            var modifiedClass = targetClass.WithMembers(newMembers);

            return root.ReplaceNode(targetClass, modifiedClass);
        }

        private static MemberDeclarationSyntax? CreateExplicitImplementation(
            MemberDeclarationSyntax interfaceMember,
            string interfaceName)
        {
            return interfaceMember switch
            {
                MethodDeclarationSyntax method => CreateExplicitMethodImplementation(method, interfaceName),
                PropertyDeclarationSyntax property => CreateExplicitPropertyImplementation(property, interfaceName),
                EventDeclarationSyntax eventDecl => CreateExplicitEventImplementation(eventDecl, interfaceName),
                _ => null
            };
        }

        private static MethodDeclarationSyntax CreateExplicitMethodImplementation(
            MethodDeclarationSyntax interfaceMethod,
            string interfaceName)
        {
            var explicitName = SyntaxFactory.QualifiedName(
                SyntaxFactory.IdentifierName(interfaceName),
                SyntaxFactory.IdentifierName(interfaceMethod.Identifier.ValueText));

            var throwStatement = SyntaxFactory.ThrowStatement(
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName("NotImplementedException"))
                .WithArgumentList(SyntaxFactory.ArgumentList()));

            var body = SyntaxFactory.Block(throwStatement);

            return SyntaxFactory.MethodDeclaration(interfaceMethod.ReturnType, interfaceMethod.Identifier)
                .WithExplicitInterfaceSpecifier(
                    SyntaxFactory.ExplicitInterfaceSpecifier(
                        SyntaxFactory.IdentifierName(interfaceName)))
                .WithParameterList(interfaceMethod.ParameterList)
                .WithBody(body);
        }

        private static PropertyDeclarationSyntax CreateExplicitPropertyImplementation(
            PropertyDeclarationSyntax interfaceProperty,
            string interfaceName)
        {
            var accessors = new List<AccessorDeclarationSyntax>();

            if (interfaceProperty.AccessorList != null)
            {
                foreach (var accessor in interfaceProperty.AccessorList.Accessors)
                {
                    var throwStatement = SyntaxFactory.ThrowStatement(
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.IdentifierName("NotImplementedException"))
                        .WithArgumentList(SyntaxFactory.ArgumentList()));

                    var accessorImpl = SyntaxFactory.AccessorDeclaration(accessor.Kind())
                        .WithBody(SyntaxFactory.Block(throwStatement));

                    accessors.Add(accessorImpl);
                }
            }

            return SyntaxFactory.PropertyDeclaration(interfaceProperty.Type, interfaceProperty.Identifier)
                .WithExplicitInterfaceSpecifier(
                    SyntaxFactory.ExplicitInterfaceSpecifier(
                        SyntaxFactory.IdentifierName(interfaceName)))
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
        }

        private static EventDeclarationSyntax CreateExplicitEventImplementation(
            EventDeclarationSyntax interfaceEvent,
            string interfaceName)
        {
            return SyntaxFactory.EventDeclaration(interfaceEvent.Type, interfaceEvent.Identifier)
                .WithExplicitInterfaceSpecifier(
                    SyntaxFactory.ExplicitInterfaceSpecifier(
                        SyntaxFactory.IdentifierName(interfaceName)))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static List<MemberDeclarationSyntax> CreateSampleInterfaceImplementations(string interfaceName)
        {
            var members = new List<MemberDeclarationSyntax>();

            // Create a sample method implementation
            var throwStatement = SyntaxFactory.ThrowStatement(
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName("NotImplementedException"))
                .WithArgumentList(SyntaxFactory.ArgumentList()));

            var sampleMethod = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                "SampleMethod")
                .WithExplicitInterfaceSpecifier(
                    SyntaxFactory.ExplicitInterfaceSpecifier(
                        SyntaxFactory.IdentifierName(interfaceName)))
                .WithParameterList(SyntaxFactory.ParameterList())
                .WithBody(SyntaxFactory.Block(throwStatement));

            members.Add(sampleMethod);

            return members;
        }
    }
}