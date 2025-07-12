using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspGoToDefinitionCommand : Command<LspSettings>
{
    public override int Execute(CommandContext context, LspSettings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    public async Task<int> ExecuteAsync(CommandContext context, LspSettings settings)
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

            // Request definition
            var result = await client.GoToDefinitionAsync(documentUri, position);
            
            if (result == null)
            {
                AnsiConsole.WriteLine("[yellow]No definition found at the specified position.[/]");
                return 0;
            }

            AnsiConsole.WriteLine("[green]Definitions found:[/]");
            
            if (result is JArray array)
            {
                foreach (var definition in array)
                {
                    helper.OutputLocation(definition);
                }
                
                if (array.Count == 0)
                {
                    AnsiConsole.WriteLine("[yellow]No definition found at the specified position.[/]");
                }
            }
            else if (result is JObject obj)
            {
                helper.OutputLocation(obj);
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