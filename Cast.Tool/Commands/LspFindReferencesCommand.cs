using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspFindReferencesCommand : Command<LspFindReferencesCommand.Settings>
{
    public class Settings : LspSettings
    {
        [CommandOption("--include-declaration")]
        [Description("Include the declaration in the results")]
        [DefaultValue(true)]
        public bool IncludeDeclaration { get; init; } = true;
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
            var position = helper.CreatePosition(settings);
            var languageId = helper.GetLanguageId(settings.FilePath);
            var fileContent = await File.ReadAllTextAsync(settings.FilePath);

            // Open the document
            await client.DidOpenAsync(documentUri, languageId, fileContent);

            // Give the server time to analyze the document
            await Task.Delay(1000);

            // Request references
            var result = await client.FindReferencesAsync(documentUri, position, settings.IncludeDeclaration);
            
            if (result == null)
            {
                AnsiConsole.WriteLine("[yellow]No references found at the specified position.[/]");
                return 0;
            }

            if (result is JArray array)
            {
                AnsiConsole.WriteLine($"[green]Found {array.Count} reference(s):[/]");
                foreach (var reference in array)
                {
                    helper.OutputLocation(reference);
                }
                
                if (array.Count == 0)
                {
                    AnsiConsole.WriteLine("[yellow]No references found at the specified position.[/]");
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