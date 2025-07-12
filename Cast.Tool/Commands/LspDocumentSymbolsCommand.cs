using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspDocumentSymbolsCommand : Command<LspSettings>
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
            var languageId = helper.GetLanguageId(settings.FilePath);
            var fileContent = await File.ReadAllTextAsync(settings.FilePath);

            // Open the document
            await client.DidOpenAsync(documentUri, languageId, fileContent);

            // Give the server time to analyze the document
            await Task.Delay(1000);

            // Request document symbols
            var result = await client.GetDocumentSymbolsAsync(documentUri);
            
            if (result == null)
            {
                AnsiConsole.WriteLine("[yellow]No document symbols found.[/]");
                return 0;
            }

            AnsiConsole.WriteLine($"[green]Document symbols for {Path.GetFileName(settings.FilePath)}:[/]");
            
            if (result is JArray symbolArray)
            {
                foreach (var symbol in symbolArray)
                {
                    // Check if it's a DocumentSymbol (hierarchical) or SymbolInformation (flat)
                    if (symbol["children"] != null || symbol["selectionRange"] != null)
                    {
                        helper.OutputDocumentSymbol(symbol);
                    }
                    else
                    {
                        helper.OutputSymbol(symbol);
                    }
                }
                
                if (symbolArray.Count == 0)
                {
                    AnsiConsole.WriteLine("[yellow]No document symbols found.[/]");
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