using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Cast.Tool.Commands;

public sealed class SortUsingsSettings : CommandSettings
{
    [Description("Path to the C# source file")]
    [CommandArgument(0, "<file>")]
    public string FilePath { get; init; } = "";

    [Description("Output file path (default: overwrite input)")]
    [CommandOption("-o|--output")]
    public string? OutputPath { get; init; }

    [Description("Preview changes without applying them")]
    [CommandOption("--dry-run")]
    public bool DryRun { get; init; }

    [Description("Separate System usings from other usings")]
    [CommandOption("--separate-system")]
    public bool SeparateSystem { get; init; } = true;
}

public sealed class SortUsingsCommand : Command<SortUsingsSettings>
{
    public override int Execute(CommandContext context, SortUsingsSettings settings)
    {
        try
        {
            if (!File.Exists(settings.FilePath))
            {
                AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                return 1;
            }

            var sourceText = File.ReadAllText(settings.FilePath);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetCompilationUnitRoot();

            // Get all using directives
            var usingDirectives = root.Usings.ToList();
            
            if (usingDirectives.Count <= 1)
            {
                AnsiConsole.MarkupLine("[yellow]No using statements to sort[/]");
                return 0;
            }

            // Sort the using directives
            var sortedUsings = SortUsingDirectives(usingDirectives, settings.SeparateSystem);

            // Check if already sorted
            if (AreUsingsAlreadySorted(usingDirectives, sortedUsings))
            {
                AnsiConsole.MarkupLine("[yellow]Using statements are already sorted[/]");
                return 0;
            }

            // Remove existing usings and add sorted ones
            var newRoot = root.RemoveNodes(usingDirectives, SyntaxRemoveOptions.KeepNoTrivia);
            if (newRoot == null)
            {
                AnsiConsole.MarkupLine("[red]Error: Failed to process using statements[/]");
                return 1;
            }

            // Add sorted usings at the top
            newRoot = newRoot.WithUsings(SyntaxFactory.List(sortedUsings));

            var newSourceText = newRoot.ToFullString();

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[green]Would sort {0} using statements in {1}[/]", 
                    usingDirectives.Count, settings.FilePath);
                AnsiConsole.MarkupLine("Sorted order:");
                foreach (var sortedUsing in sortedUsings)
                {
                    AnsiConsole.MarkupLine("  {0}", sortedUsing.ToString().Trim());
                }
                return 0;
            }

            var outputPath = settings.OutputPath ?? settings.FilePath;
            File.WriteAllText(outputPath, newSourceText);

            AnsiConsole.MarkupLine("[green]Successfully sorted {0} using statements in {1}[/]", 
                usingDirectives.Count, outputPath);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
            return 1;
        }
    }

    private static List<UsingDirectiveSyntax> SortUsingDirectives(List<UsingDirectiveSyntax> usings, bool separateSystem)
    {
        var result = new List<UsingDirectiveSyntax>();

        if (separateSystem)
        {
            // First, add System usings sorted alphabetically
            var systemUsings = usings.Where(u => u.Name?.ToString().StartsWith("System") == true)
                                   .OrderBy(u => u.Name?.ToString())
                                   .ToList();

            // Then add non-System usings sorted alphabetically
            var otherUsings = usings.Where(u => u.Name?.ToString().StartsWith("System") != true)
                                   .OrderBy(u => u.Name?.ToString())
                                   .ToList();

            result.AddRange(systemUsings);
            
            // Add blank line between System and other usings if both exist
            if (systemUsings.Any() && otherUsings.Any())
            {
                // Add the first non-system using with extra leading trivia for spacing
                if (otherUsings.Count > 0)
                {
                    var firstOther = otherUsings[0];
                    var withSpacing = firstOther.WithLeadingTrivia(
                        SyntaxFactory.ElasticCarriageReturnLineFeed,
                        SyntaxFactory.ElasticCarriageReturnLineFeed);
                    otherUsings[0] = withSpacing;
                }
            }
            
            result.AddRange(otherUsings);
        }
        else
        {
            // Sort all usings alphabetically without separation
            result = usings.OrderBy(u => u.Name?.ToString()).ToList();
        }

        return result;
    }

    private static bool AreUsingsAlreadySorted(List<UsingDirectiveSyntax> original, List<UsingDirectiveSyntax> sorted)
    {
        if (original.Count != sorted.Count) return false;

        for (int i = 0; i < original.Count; i++)
        {
            if (original[i].Name?.ToString() != sorted[i].Name?.ToString())
            {
                return false;
            }
        }

        return true;
    }
}