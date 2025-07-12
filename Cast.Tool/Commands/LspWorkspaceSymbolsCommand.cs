using Cast.Tool.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Commands;

public class LspWorkspaceSymbolsCommand : Command<LspWorkspaceSymbolsCommand.Settings>
{
    public class Settings : LspSettings
    {
        [CommandArgument(0, "[QUERY]")]
        [Description("Search query for workspace symbols (overrides --query)")]
        public string? QueryArgument { get; init; }

        [CommandOption("-w|--workspace")]
        [Description("Workspace root directory")]
        public string? WorkspaceRoot { get; init; }
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
            var query = settings.QueryArgument ?? settings.Query ?? "";
            
            using var client = await helper.CreateLspClientAsync(settings);
            if (client == null) return 1;

            // Give the server time to initialize
            await Task.Delay(1000);

            // Request workspace symbols
            var result = await client.GetWorkspaceSymbolsAsync(query);
            
            if (result == null)
            {
                AnsiConsole.WriteLine($"[yellow]No workspace symbols found{(string.IsNullOrEmpty(query) ? "" : $" for query '{query}'")}.[/]");
                return 0;
            }

            if (result is JArray symbolArray)
            {
                AnsiConsole.WriteLine($"[green]Found {symbolArray.Count} workspace symbol(s):{(string.IsNullOrEmpty(query) ? "" : $" for '{query}'")}[/]");
                
                // Group symbols by kind for better organization
                var groupedSymbols = symbolArray.GroupBy(s => s["kind"]?.ToString() ?? "Unknown").OrderBy(g => g.Key);
                
                foreach (var group in groupedSymbols)
                {
                    AnsiConsole.WriteLine($"\n[cyan]{group.Key}:[/]");
                    foreach (var symbol in group.OrderBy(s => s["name"]?.ToString()))
                    {
                        helper.OutputSymbol(symbol);
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}