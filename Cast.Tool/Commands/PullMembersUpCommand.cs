using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class PullMembersUpCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--source-type")]
        [Description("Name of the source type to pull members from")]
        public string SourceType { get; set; } = string.Empty;

        [CommandOption("--target-type")]
        [Description("Name of the target base type or interface to pull members to")]
        public string TargetType { get; set; } = string.Empty;

        [CommandOption("--members")]
        [Description("Comma-separated list of member names to pull up (defaults to all public members)")]
        public string Members { get; set; } = string.Empty;

        [CommandOption("--output")]
        [Description("Output file path (default: modify in place)")]
        public string? OutputPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class PullMembersUpCommand : Command<PullMembersUpCommandSettings>
    {
        public override int Execute(CommandContext context, PullMembersUpCommandSettings settings)
        {
            try
            {
                if (!File.Exists(settings.FilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.SourceType))
                {
                    AnsiConsole.MarkupLine("[red]Error: Source type name is required[/]");
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.TargetType))
                {
                    AnsiConsole.MarkupLine("[red]Error: Target type name is required[/]");
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var membersToMove = new List<string>();
                if (!string.IsNullOrWhiteSpace(settings.Members))
                {
                    membersToMove = settings.Members.Split(',')
                        .Select(m => m.Trim())
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();
                }

                var modifiedRoot = PullMembersUp(root, settings.SourceType, settings.TargetType, membersToMove);

                if (settings.DryRun)
                {
                    AnsiConsole.MarkupLine("[green]Would pull members from '{0}' up to '{1}' in {2}[/]", 
                        settings.SourceType, settings.TargetType, settings.FilePath);
                    
                    if (membersToMove.Any())
                    {
                        AnsiConsole.MarkupLine("[yellow]Members to move: {0}[/]", string.Join(", ", membersToMove));
                    }
                    
                    // Show diff for the changes
                    var dryRunModifiedCode = modifiedRoot.ToFullString();
                    AnsiConsole.WriteLine();
                    DiffUtility.DisplayDiff(sourceCode, dryRunModifiedCode, settings.FilePath);
                    
                    return 0;
                }

                var outputPath = settings.OutputPath ?? settings.FilePath;
                var modifiedCode = modifiedRoot.ToFullString();
                File.WriteAllText(outputPath, modifiedCode);

                AnsiConsole.MarkupLine("[green]Successfully pulled members from '{0}' up to '{1}' in {2}[/]", 
                    settings.SourceType, settings.TargetType, outputPath);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static CompilationUnitSyntax PullMembersUp(
            CompilationUnitSyntax root,
            string sourceTypeName,
            string targetTypeName,
            List<string> membersToMove)
        {
            // Find source and target types
            var sourceType = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.ValueText == sourceTypeName);

            var targetType = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.ValueText == targetTypeName);

            if (sourceType == null)
            {
                throw new InvalidOperationException($"Source type '{sourceTypeName}' not found");
            }

            if (targetType == null)
            {
                throw new InvalidOperationException($"Target type '{targetTypeName}' not found");
            }

            // Determine which members to move
            var membersToMoveUp = new List<MemberDeclarationSyntax>();
            
            foreach (var member in sourceType.Members)
            {
                var memberName = GetMemberName(member);
                
                if (membersToMove.Any())
                {
                    // Only move specified members
                    if (membersToMove.Contains(memberName))
                    {
                        membersToMoveUp.Add(member);
                    }
                }
                else
                {
                    // Move all public members
                    if (IsPublicMember(member) && CanMoveUp(member, targetType))
                    {
                        membersToMoveUp.Add(member);
                    }
                }
            }

            if (!membersToMoveUp.Any())
            {
                throw new InvalidOperationException("No eligible members found to move up");
            }

            // Transform members for the target type
            var transformedMembers = new List<MemberDeclarationSyntax>();
            
            foreach (var member in membersToMoveUp)
            {
                var transformedMember = TransformMemberForTarget(member, targetType);
                if (transformedMember != null)
                {
                    transformedMembers.Add(transformedMember);
                }
            }

            // Add members to target type
            var newTargetMembers = targetType.Members.AddRange(transformedMembers);
            var modifiedTargetType = targetType.WithMembers(newTargetMembers);

            // Remove members from source type
            var modifiedSourceType = sourceType;
            foreach (var member in membersToMoveUp)
            {
                modifiedSourceType = modifiedSourceType.RemoveNode(member, SyntaxRemoveOptions.KeepNoTrivia)!;
            }

            // Replace both types in the compilation unit
            var result = root.ReplaceNode(targetType, modifiedTargetType);
            result = result.ReplaceNode(
                result.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .First(t => t.Identifier.ValueText == sourceTypeName),
                modifiedSourceType);

            return result;
        }

        private static bool IsPublicMember(MemberDeclarationSyntax member)
        {
            return member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
        }

        private static bool CanMoveUp(MemberDeclarationSyntax member, TypeDeclarationSyntax targetType)
        {
            // Don't move constructors, destructors, or static members
            if (member is ConstructorDeclarationSyntax || 
                member is DestructorDeclarationSyntax ||
                member.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            {
                return false;
            }

            // For interfaces, only move abstract members
            if (targetType is InterfaceDeclarationSyntax)
            {
                return member is MethodDeclarationSyntax ||
                       member is PropertyDeclarationSyntax ||
                       member is EventDeclarationSyntax;
            }

            return true;
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

        private static MemberDeclarationSyntax? TransformMemberForTarget(
            MemberDeclarationSyntax member,
            TypeDeclarationSyntax targetType)
        {
            // If target is an interface, convert to interface signatures
            if (targetType is InterfaceDeclarationSyntax)
            {
                return member switch
                {
                    MethodDeclarationSyntax method => ConvertMethodToInterfaceSignature(method),
                    PropertyDeclarationSyntax property => ConvertPropertyToInterfaceSignature(property),
                    EventDeclarationSyntax eventDecl => ConvertEventToInterfaceSignature(eventDecl),
                    _ => null
                };
            }

            // For base classes, make members virtual if they aren't already
            return member switch
            {
                MethodDeclarationSyntax method => MakeMethodVirtual(method),
                PropertyDeclarationSyntax property => MakePropertyVirtual(property),
                _ => member
            };
        }

        private static MethodDeclarationSyntax ConvertMethodToInterfaceSignature(MethodDeclarationSyntax method)
        {
            return SyntaxFactory.MethodDeclaration(method.ReturnType, method.Identifier)
                .WithParameterList(method.ParameterList)
                .WithTypeParameterList(method.TypeParameterList)
                .WithConstraintClauses(method.ConstraintClauses)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static PropertyDeclarationSyntax ConvertPropertyToInterfaceSignature(PropertyDeclarationSyntax property)
        {
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

        private static EventDeclarationSyntax ConvertEventToInterfaceSignature(EventDeclarationSyntax eventDecl)
        {
            return SyntaxFactory.EventDeclaration(eventDecl.Type, eventDecl.Identifier)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static MethodDeclarationSyntax MakeMethodVirtual(MethodDeclarationSyntax method)
        {
            if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword) || 
                                           m.IsKind(SyntaxKind.AbstractKeyword) ||
                                           m.IsKind(SyntaxKind.OverrideKeyword)))
            {
                var newModifiers = method.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.VirtualKeyword));
                return method.WithModifiers(newModifiers);
            }
            return method;
        }

        private static PropertyDeclarationSyntax MakePropertyVirtual(PropertyDeclarationSyntax property)
        {
            if (!property.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword) || 
                                             m.IsKind(SyntaxKind.AbstractKeyword) ||
                                             m.IsKind(SyntaxKind.OverrideKeyword)))
            {
                var newModifiers = property.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.VirtualKeyword));
                return property.WithModifiers(newModifiers);
            }
            return property;
        }
    }
}