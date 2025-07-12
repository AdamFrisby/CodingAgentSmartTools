using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspHoverCommand : Command<LspSettings>
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

            // Request hover information
            var result = await client.GetHoverInfoAsync(documentUri, position);
            
            if (result == null)
            {
                AnsiConsole.WriteLine("[yellow]No hover information available at the specified position.[/]");
                return 0;
            }

            AnsiConsole.WriteLine("[green]Hover Information:[/]");
            
            if (result is JObject hoverObj)
            {
                var contents = hoverObj["contents"];
                if (contents != null)
                {
                    if (contents is JArray contentArray)
                    {
                        foreach (var content in contentArray)
                        {
                            OutputHoverContent(content);
                        }
                    }
                    else if (contents is JObject contentObj)
                    {
                        OutputHoverContent(contentObj);
                    }
                    else if (contents is JValue contentValue)
                    {
                        AnsiConsole.WriteLine(contentValue.ToString());
                    }
                }

                var range = hoverObj["range"];
                if (range != null)
                {
                    var startLine = (range["start"]?["line"]?.Value<int>() ?? 0) + 1;
                    var startCol = (range["start"]?["character"]?.Value<int>() ?? 0) + 1;
                    var endLine = (range["end"]?["line"]?.Value<int>() ?? 0) + 1;
                    var endCol = (range["end"]?["character"]?.Value<int>() ?? 0) + 1;
                    AnsiConsole.WriteLine($"[grey]Range: {startLine}:{startCol} - {endLine}:{endCol}[/]");
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

    private void OutputHoverContent(JToken content)
    {
        try
        {
            if (content is JObject obj)
            {
                var kind = obj["kind"]?.ToString();
                var value = obj["value"]?.ToString();
                var language = obj["language"]?.ToString();

                if (!string.IsNullOrEmpty(language))
                {
                    AnsiConsole.WriteLine($"[grey]Language: {language}[/]");
                }
                if (!string.IsNullOrEmpty(kind))
                {
                    AnsiConsole.WriteLine($"[grey]({kind})[/]");
                }
                if (!string.IsNullOrEmpty(value))
                {
                    AnsiConsole.WriteLine(value);
                }
            }
            else if (content is JValue val)
            {
                AnsiConsole.WriteLine(val.ToString());
            }
        }
        catch (Exception)
        {
            AnsiConsole.WriteLine($"Raw content: {content}");
        }
    }
}