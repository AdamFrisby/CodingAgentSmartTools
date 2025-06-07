using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class MakeMemberStaticCommand : Command<MakeMemberStaticCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) where the member is defined")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-o|--output")]
        [Description("Output file path (defaults to overwriting the input file)")]
        public string? OutputPath { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show what changes would be made without applying them")]
        [DefaultValue(false)]
        public bool DryRun { get; init; } = false;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            ValidateInputs(settings);

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);

            var position = engine.GetTextSpanFromPosition(tree, settings.LineNumber, 0);
            var root = await tree.GetRootAsync();
            var node = root.FindNode(position);

            // Find the member declaration (method, property, field)
            var memberDeclaration = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            if (memberDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: No member declaration found at the specified location[/]");
                return 1;
            }

            var memberName = GetMemberName(memberDeclaration);
            if (string.IsNullOrEmpty(memberName))
            {
                AnsiConsole.WriteLine("[red]Error: Could not determine member name[/]");
                return 1;
            }

            // Check if already static
            var modifiers = GetModifiers(memberDeclaration);
            if (modifiers.Any(SyntaxKind.StaticKeyword))
            {
                AnsiConsole.WriteLine($"[yellow]Member '{memberName}' is already static[/]");
                return 0;
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would make member '{memberName}' static[/]");
                return 0;
            }

            // Add static modifier
            var newModifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space));
            var newMemberDeclaration = WithModifiers(memberDeclaration, newModifiers);

            if (newMemberDeclaration == null)
            {
                AnsiConsole.WriteLine("[red]Error: Could not add static modifier to member[/]");
                return 1;
            }

            var newRoot = root.ReplaceNode(memberDeclaration, newMemberDeclaration);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully made member '{memberName}' static in {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
            EventDeclarationSyntax eventDecl => eventDecl.Identifier.ValueText,
            _ => null
        };
    }

    private static SyntaxTokenList GetModifiers(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Modifiers,
            PropertyDeclarationSyntax property => property.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            EventDeclarationSyntax eventDecl => eventDecl.Modifiers,
            _ => default
        };
    }

    private static MemberDeclarationSyntax? WithModifiers(MemberDeclarationSyntax member, SyntaxTokenList modifiers)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.WithModifiers(modifiers),
            PropertyDeclarationSyntax property => property.WithModifiers(modifiers),
            FieldDeclarationSyntax field => field.WithModifiers(modifiers),
            EventDeclarationSyntax eventDecl => eventDecl.WithModifiers(modifiers),
            _ => null
        };
    }

    private void ValidateInputs(Settings settings)
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
    }
}