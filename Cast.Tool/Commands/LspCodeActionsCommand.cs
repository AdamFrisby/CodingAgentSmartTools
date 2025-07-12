using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspCodeActionsCommand : Command<LspCodeActionsCommand.Settings>
{
    public class Settings : LspSettings
    {
        [CommandOption("--end-line")]
        [Description("End line number (0-based) for range selection")]
        public int? EndLineNumber { get; init; }

        [CommandOption("--end-column")]
        [Description("End column number (0-based) for range selection")]
        public int? EndColumnNumber { get; init; }
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
            var fileContent = await File.ReadAllTextAsync(settings.FilePath);

            // Open the document
            await client.DidOpenAsync(documentUri, languageId, fileContent);

            // Give the server time to analyze the document
            await Task.Delay(1000);

            // Create range for code actions
            var startPosition = helper.CreatePosition(settings);
            var endPosition = settings.EndLineNumber.HasValue || settings.EndColumnNumber.HasValue
                ? new LspPosition(settings.EndLineNumber ?? settings.LineNumber, settings.EndColumnNumber ?? settings.ColumnNumber)
                : startPosition;

            var range = new LspRange(startPosition, endPosition);

            // Request code actions
            var result = await client.GetCodeActionsAsync(documentUri, range);
            
            if (result == null)
            {
                AnsiConsole.WriteLine("[yellow]No code actions available for the specified range.[/]");
                return 0;
            }

            if (result is JArray actionsArray)
            {
                AnsiConsole.WriteLine($"[green]Available code actions ({actionsArray.Count}):[/]");
                
                var actionIndex = 1;
                foreach (var action in actionsArray)
                {
                    var title = action["title"]?.ToString();
                    var kind = action["kind"]?.ToString();
                    var command = action["command"];
                    var edit = action["edit"];
                    var diagnostics = action["diagnostics"] as JArray;

                    if (!string.IsNullOrEmpty(title))
                    {
                        AnsiConsole.WriteLine($"{actionIndex}. [cyan]{title}[/]");
                        
                        if (!string.IsNullOrEmpty(kind))
                        {
                            AnsiConsole.WriteLine($"   Kind: {kind}");
                        }
                        
                        if (diagnostics != null && diagnostics.Count > 0)
                        {
                            AnsiConsole.WriteLine($"   Fixes {diagnostics.Count} diagnostic(s)");
                        }

                        if (edit?["changes"] != null)
                        {
                            var changes = edit["changes"] as JObject;
                            if (changes != null)
                            {
                                AnsiConsole.WriteLine($"   Affects {changes.Count} file(s)");
                            }
                        }

                        if (command != null)
                        {
                            var commandName = command["command"]?.ToString();
                            if (!string.IsNullOrEmpty(commandName))
                            {
                                AnsiConsole.WriteLine($"   Command: {commandName}");
                            }
                        }
                    }
                    
                    actionIndex++;
                    AnsiConsole.WriteLine();
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
}