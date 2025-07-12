using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspFormatDocumentCommand : Command<LspFormatDocumentCommand.Settings>
{
    public class Settings : LspSettings
    {
        [CommandOption("--tab-size")]
        [Description("Tab size for formatting")]
        [DefaultValue(4)]
        public int TabSize { get; init; } = 4;

        [CommandOption("--insert-spaces")]
        [Description("Use spaces instead of tabs")]
        [DefaultValue(true)]
        public bool InsertSpaces { get; init; } = true;

        [CommandOption("--output")]
        [Description("Output formatted content to a file (default: overwrite original)")]
        public string? OutputFile { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show formatting changes without applying them")]
        public bool DryRun { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var helper = new LspHelper();
        try
        {
            helper.ValidateInputs(settings);

            using var client = await helper.CreateLspClientAsync(settings);
            if (client == null) return 1;

            var documentUri = helper.CreateDocumentUri(settings.FilePath);
            var languageId = helper.GetLanguageId(settings.FilePath);
            var originalContent = await File.ReadAllTextAsync(settings.FilePath);

            // Open the document
            await client.DidOpenAsync(documentUri, languageId, originalContent);

            // Give the server time to analyze the document
            await Task.Delay(1000);

            // Request document formatting
            var result = await client.FormatDocumentAsync(documentUri);
            
            if (result == null)
            {
                AnsiConsole.WriteLine("[yellow]No formatting changes suggested by the LSP server.[/]");
                return 0;
            }

            if (result is JArray editsArray)
            {
                if (editsArray.Count == 0)
                {
                    AnsiConsole.WriteLine("[yellow]No formatting changes suggested by the LSP server.[/]");
                    return 0;
                }

                // Apply the text edits
                var formattedContent = ApplyTextEdits(originalContent, editsArray);

                if (settings.DryRun)
                {
                    AnsiConsole.WriteLine("[green]Formatting changes (dry run):[/]");
                    AnsiConsole.WriteLine($"[cyan]Original length:[/] {originalContent.Length} characters");
                    AnsiConsole.WriteLine($"[cyan]Formatted length:[/] {formattedContent.Length} characters");
                    AnsiConsole.WriteLine($"[cyan]Number of edits:[/] {editsArray.Count}");
                    
                    // Show a diff preview (simplified)
                    if (originalContent != formattedContent)
                    {
                        AnsiConsole.WriteLine("\n[yellow]Content will be changed[/]");
                    }
                    else
                    {
                        AnsiConsole.WriteLine("\n[green]No changes needed[/]");
                    }
                }
                else
                {
                    var outputPath = settings.OutputFile ?? settings.FilePath;
                    await File.WriteAllTextAsync(outputPath, formattedContent);
                    
                    AnsiConsole.WriteLine($"[green]Document formatted successfully.[/]");
                    AnsiConsole.WriteLine($"[cyan]Output written to:[/] {outputPath}");
                    AnsiConsole.WriteLine($"[cyan]Applied {editsArray.Count} edit(s)[/]");
                }
            }

            // Close the document
            await client.DidCloseAsync(documentUri);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private string ApplyTextEdits(string originalText, JArray edits)
    {
        // Sort edits by range (from end to beginning) to avoid offset issues
        var sortedEdits = edits.OrderByDescending(e => e["range"]?["start"]?["line"]?.Value<int>() ?? 0)
                               .ThenByDescending(e => e["range"]?["start"]?["character"]?.Value<int>() ?? 0)
                               .ToList();

        var lines = originalText.Split('\n');
        
        foreach (var edit in sortedEdits)
        {
            try
            {
                var range = edit["range"];
                var newText = edit["newText"]?.ToString() ?? "";
                
                if (range == null) continue;

                var startLine = range["start"]?["line"]?.Value<int>() ?? 0;
                var startChar = range["start"]?["character"]?.Value<int>() ?? 0;
                var endLine = range["end"]?["line"]?.Value<int>() ?? 0;
                var endChar = range["end"]?["character"]?.Value<int>() ?? 0;

                if (startLine == endLine)
                {
                    // Single line edit
                    if (startLine < lines.Length)
                    {
                        var line = lines[startLine];
                        if (startChar <= line.Length && endChar <= line.Length)
                        {
                            lines[startLine] = line.Substring(0, startChar) + newText + line.Substring(endChar);
                        }
                    }
                }
                else
                {
                    // Multi-line edit - simplified approach
                    if (startLine < lines.Length && endLine < lines.Length)
                    {
                        var newLines = new List<string>();
                        
                        // Add lines before the edit
                        for (int i = 0; i < startLine; i++)
                        {
                            newLines.Add(lines[i]);
                        }
                        
                        // Add the edited content
                        var startLinePrefix = startChar < lines[startLine].Length ? lines[startLine].Substring(0, startChar) : lines[startLine];
                        var endLineSuffix = endChar < lines[endLine].Length ? lines[endLine].Substring(endChar) : "";
                        
                        var editLines = newText.Split('\n');
                        if (editLines.Length == 1)
                        {
                            newLines.Add(startLinePrefix + editLines[0] + endLineSuffix);
                        }
                        else
                        {
                            newLines.Add(startLinePrefix + editLines[0]);
                            for (int i = 1; i < editLines.Length - 1; i++)
                            {
                                newLines.Add(editLines[i]);
                            }
                            newLines.Add(editLines[editLines.Length - 1] + endLineSuffix);
                        }
                        
                        // Add lines after the edit
                        for (int i = endLine + 1; i < lines.Length; i++)
                        {
                            newLines.Add(lines[i]);
                        }
                        
                        lines = newLines.ToArray();
                    }
                }
            }
            catch (Exception)
            {
                // Skip problematic edits
                continue;
            }
        }

        return string.Join('\n', lines);
    }
}