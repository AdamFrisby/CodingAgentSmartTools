using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class ExtractBaseClassCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--class-name")]
        [Description("Name of the class to extract from")]
        public string ClassName { get; set; } = string.Empty;

        [CommandOption("--base-class-name")]
        [Description("Name for the new base class")]
        public string BaseClassName { get; set; } = string.Empty;

        [CommandOption("--members")]
        [Description("Comma-separated list of member names to move to base class")]
        public string Members { get; set; } = string.Empty;

        [CommandOption("--output")]
        [Description("Output file path (default: modify in place)")]
        public string? OutputPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class ExtractBaseClassCommand : Command<ExtractBaseClassCommandSettings>
    {
        public override int Execute(CommandContext context, ExtractBaseClassCommandSettings settings)
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

                if (string.IsNullOrWhiteSpace(settings.BaseClassName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Base class name is required[/]");
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

                var (modifiedRoot, baseClassCode) = ExtractBaseClass(root, targetClass, settings.BaseClassName, membersToExtract);

                if (settings.DryRun)
                {
                    AnsiConsole.MarkupLine("[green]Would extract base class '{0}' from class '{1}' in {2}[/]", 
                        settings.BaseClassName, settings.ClassName, settings.FilePath);
                    
                    if (membersToExtract.Any())
                    {
                        AnsiConsole.MarkupLine("[yellow]Members to extract: {0}[/]", string.Join(", ", membersToExtract));
                    }
                    
                    // Use the new multi-file diff format
                    var dryRunModifiedCode = modifiedRoot.ToFullString();
                    var dryRunBaseClassPath = Path.Combine(Path.GetDirectoryName(settings.FilePath) ?? ".", $"{settings.BaseClassName}.cs");
                    
                    var fileChanges = new Dictionary<string, (string original, string modified)>
                    {
                        { settings.FilePath, (sourceCode, dryRunModifiedCode) },
                        { dryRunBaseClassPath, ("", baseClassCode) }
                    };
                    
                    AnsiConsole.WriteLine();
                    DiffUtility.DisplayMultiFileDiff(fileChanges);
                    
                    return 0;
                }

                var outputPath = settings.OutputPath ?? settings.FilePath;
                var modifiedCode = modifiedRoot.ToFullString();
                File.WriteAllText(outputPath, modifiedCode);

                // Write base class to a separate file
                var baseClassPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", $"{settings.BaseClassName}.cs");
                File.WriteAllText(baseClassPath, baseClassCode);

                AnsiConsole.MarkupLine("[green]Successfully extracted base class '{0}' from '{1}' in {2}[/]", 
                    settings.BaseClassName, settings.ClassName, outputPath);
                AnsiConsole.MarkupLine("[green]Base class written to: {0}[/]", baseClassPath);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static (CompilationUnitSyntax modifiedRoot, string baseClassCode) ExtractBaseClass(
            CompilationUnitSyntax root,
            ClassDeclarationSyntax targetClass,
            string baseClassName,
            List<string> membersToExtract)
        {
            var membersToMove = new List<MemberDeclarationSyntax>();
            var remainingMembers = new List<MemberDeclarationSyntax>();

            // If no specific members are specified, extract all public members
            if (!membersToExtract.Any())
            {
                foreach (var member in targetClass.Members)
                {
                    if (HasPublicModifier(member))
                    {
                        membersToMove.Add(member);
                    }
                    else
                    {
                        remainingMembers.Add(member);
                    }
                }
            }
            else
            {
                foreach (var member in targetClass.Members)
                {
                    var memberName = GetMemberName(member);
                    if (membersToExtract.Contains(memberName))
                    {
                        membersToMove.Add(member);
                    }
                    else
                    {
                        remainingMembers.Add(member);
                    }
                }
            }

            // Create base class
            var baseClass = SyntaxFactory.ClassDeclaration(baseClassName)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithMembers(SyntaxFactory.List(membersToMove));

            // Create namespace for base class if original class is in a namespace
            var namespaceDeclaration = targetClass.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
            var baseClassRoot = SyntaxFactory.CompilationUnit();

            // Copy using directives
            baseClassRoot = baseClassRoot.WithUsings(root.Usings);

            if (namespaceDeclaration != null)
            {
                var baseNamespace = SyntaxFactory.NamespaceDeclaration(namespaceDeclaration.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(baseClass));
                baseClassRoot = baseClassRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(baseNamespace));
            }
            else
            {
                baseClassRoot = baseClassRoot.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(baseClass));
            }

            // Modify original class to inherit from base class and remove extracted members
            var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName(baseClassName));
            var baseList = targetClass.BaseList ?? SyntaxFactory.BaseList();
            baseList = baseList.WithTypes(baseList.Types.Insert(0, baseType));

            var modifiedClass = targetClass
                .WithBaseList(baseList)
                .WithMembers(SyntaxFactory.List(remainingMembers));

            var modifiedRoot = root.ReplaceNode(targetClass, modifiedClass);

            return (modifiedRoot, baseClassRoot.NormalizeWhitespace().ToFullString());
        }

        private static bool HasPublicModifier(MemberDeclarationSyntax member)
        {
            return member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
        }

        private static string GetMemberName(MemberDeclarationSyntax member)
        {
            return member switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? "",
                EventDeclarationSyntax eventDecl => eventDecl.Identifier.ValueText,
                _ => ""
            };
        }
    }
}