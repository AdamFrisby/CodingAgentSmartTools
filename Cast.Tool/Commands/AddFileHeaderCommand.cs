using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands;

public class AddFileHeaderCommand : Command<AddFileHeaderCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to refactor")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("--header-text")]
        [Description("Custom header text to add (supports multi-line with \\n)")]
        public string? HeaderText { get; init; }

        [CommandOption("--header-file")]
        [Description("Path to a file containing the header text")]
        public string? HeaderFile { get; init; }

        [CommandOption("--copyright")]
        [Description("Add a standard copyright header with the given owner")]
        public string? Copyright { get; init; }

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

            var headerText = await GetHeaderText(settings);
            if (string.IsNullOrWhiteSpace(headerText))
            {
                AnsiConsole.WriteLine("[red]Error: No header text provided. Use --header-text, --header-file, or --copyright[/]");
                return 1;
            }

            var engine = new RefactoringEngine();
            var (document, tree, model) = await engine.LoadDocumentAsync(settings.FilePath);

            var root = await tree.GetRootAsync();

            // Check if file already has a header comment
            var firstToken = root.GetFirstToken();
            var hasExistingHeader = firstToken.HasLeadingTrivia && 
                firstToken.LeadingTrivia.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || 
                                                   t.IsKind(SyntaxKind.MultiLineCommentTrivia));

            if (hasExistingHeader && !settings.DryRun)
            {
                AnsiConsole.WriteLine("[yellow]File already appears to have a header comment. Adding new header at the top.[/]");
            }

            if (settings.DryRun)
            {
                AnsiConsole.WriteLine($"[green]Would add file header to {settings.FilePath}[/]");
                AnsiConsole.WriteLine("[dim]Header preview:[/]");
                AnsiConsole.WriteLine(headerText);
                return 0;
            }

            // Create the header comment
            var newRoot = AddHeaderToFile(root, headerText);
            var result = newRoot.ToFullString();

            var outputPath = settings.OutputPath ?? settings.FilePath;
            await File.WriteAllTextAsync(outputPath, result);

            AnsiConsole.WriteLine($"[green]Successfully added file header to {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<string> GetHeaderText(Settings settings)
    {
        // Priority: header-file > header-text > copyright
        if (!string.IsNullOrWhiteSpace(settings.HeaderFile))
        {
            if (!File.Exists(settings.HeaderFile))
            {
                throw new FileNotFoundException($"Header file not found: {settings.HeaderFile}");
            }
            return await File.ReadAllTextAsync(settings.HeaderFile);
        }

        if (!string.IsNullOrWhiteSpace(settings.HeaderText))
        {
            return settings.HeaderText.Replace("\\n", Environment.NewLine);
        }

        if (!string.IsNullOrWhiteSpace(settings.Copyright))
        {
            var year = DateTime.Now.Year;
            return $"// Copyright (c) {year} {settings.Copyright}. All rights reserved.";
        }

        return string.Empty;
    }

    private SyntaxNode AddHeaderToFile(SyntaxNode root, string headerText)
    {
        // Create comment lines
        var headerLines = headerText.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
        var commentTrivia = new List<SyntaxTrivia>();

        foreach (var line in headerLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                // Empty line - just add line break
                commentTrivia.Add(SyntaxFactory.Comment("//"));
                commentTrivia.Add(SyntaxFactory.LineFeed);
            }
            else
            {
                // Comment line
                var commentText = line.TrimStart();
                if (!commentText.StartsWith("//"))
                {
                    commentText = "// " + commentText;
                }
                commentTrivia.Add(SyntaxFactory.Comment(commentText));
                commentTrivia.Add(SyntaxFactory.LineFeed);
            }
        }

        // Add an extra line break after the header
        commentTrivia.Add(SyntaxFactory.LineFeed);

        // Get the first token and add our header as leading trivia
        var firstToken = root.GetFirstToken();
        var existingTrivia = firstToken.LeadingTrivia.ToList();
        
        // Combine header trivia with existing trivia
        var newTrivia = SyntaxFactory.TriviaList(commentTrivia.Concat(existingTrivia));
        var newFirstToken = firstToken.WithLeadingTrivia(newTrivia);

        return root.ReplaceToken(firstToken, newFirstToken);
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

        // Ensure at least one header option is provided
        if (string.IsNullOrWhiteSpace(settings.HeaderText) && 
            string.IsNullOrWhiteSpace(settings.HeaderFile) && 
            string.IsNullOrWhiteSpace(settings.Copyright))
        {
            throw new ArgumentException("At least one of --header-text, --header-file, or --copyright must be provided");
        }
    }
}