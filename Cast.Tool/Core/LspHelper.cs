using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Core;

public class LspHelper
{
    public void ValidateInputs(LspSettings settings)
    {
        if (!File.Exists(settings.FilePath))
        {
            throw new FileNotFoundException($"File not found: {settings.FilePath}");
        }

        if (settings.LineNumber < 0)
        {
            throw new ArgumentException("Line number must be 0 or greater");
        }

        if (settings.ColumnNumber < 0)
        {
            throw new ArgumentException("Column number must be 0 or greater");
        }
    }

    public LspPosition CreatePosition(LspSettings settings)
    {
        return new LspPosition(settings.LineNumber, settings.ColumnNumber);
    }

    public string CreateDocumentUri(string filePath)
    {
        return new Uri(Path.GetFullPath(filePath)).ToString();
    }

    public string GetLanguageId(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "csharp",
            ".ts" => "typescript",
            ".js" => "javascript",
            ".py" => "python",
            ".java" => "java",
            ".cpp" or ".c" or ".h" => "cpp",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".php" => "php",
            _ => "plaintext"
        };
    }

    public async Task<LspClient?> CreateLspClientAsync(LspSettings settings)
    {
        var serverPath = settings.ServerPath ?? GetDefaultServerPath(settings.FilePath);
        if (string.IsNullOrEmpty(serverPath))
        {
            AnsiConsole.WriteLine("[red]Error: No LSP server specified and no default server found for this file type.[/]");
            return null;
        }

        var serverArgs = settings.ServerArgs?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var client = new LspClient(serverPath, serverArgs);

        if (!await client.StartAsync())
        {
            AnsiConsole.WriteLine($"[red]Error: Failed to start LSP server: {serverPath}[/]");
            client.Dispose();
            return null;
        }

        return client;
    }

    public string? GetDefaultServerPath(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => FindExecutable("csharp-ls") ?? FindExecutable("omnisharp"),
            ".ts" or ".js" => FindExecutable("typescript-language-server"),
            ".py" => FindExecutable("pylsp") ?? FindExecutable("pyright"),
            ".java" => FindExecutable("jdtls"),
            ".cpp" or ".c" or ".h" => FindExecutable("clangd"),
            ".go" => FindExecutable("gopls"),
            ".rs" => FindExecutable("rust-analyzer"),
            ".rb" => FindExecutable("solargraph"),
            ".php" => FindExecutable("intelephense"),
            _ => null
        };
    }

    public string? FindExecutable(string name)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        var extensions = Environment.OSVersion.Platform == PlatformID.Win32NT 
            ? new[] { ".exe", ".cmd", ".bat" } 
            : new[] { "" };

        foreach (var path in paths)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(path, name + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    public void OutputLocation(JToken location, string? prefix = null)
    {
        try
        {
            var uri = location["uri"]?.ToString();
            var range = location["range"];
            if (uri != null && range != null)
            {
                var uriObj = new Uri(uri);
                var filePath = uriObj.IsFile ? uriObj.LocalPath : uri;
                var line = (range["start"]?["line"]?.Value<int>() ?? 0) + 1; // Convert to 1-based for display
                var column = (range["start"]?["character"]?.Value<int>() ?? 0) + 1; // Convert to 1-based for display
                
                var prefixText = prefix != null ? $"{prefix}: " : "";
                Console.WriteLine($"{prefixText}{filePath}:{line}:{column}");
            }
        }
        catch (Exception)
        {
            Console.WriteLine($"Error parsing location: {location}");
        }
    }

    public void OutputSymbol(JToken symbol)
    {
        try
        {
            var name = symbol["name"]?.ToString();
            var kind = symbol["kind"]?.ToString();
            var location = symbol["location"];
            
            if (location != null && name != null)
            {
                var uri = location["uri"]?.ToString();
                var range = location["range"];
                if (uri != null && range != null)
                {
                    var uriObj = new Uri(uri);
                    var filePath = uriObj.IsFile ? uriObj.LocalPath : uri;
                    var line = (range["start"]?["line"]?.Value<int>() ?? 0) + 1;
                    var column = (range["start"]?["character"]?.Value<int>() ?? 0) + 1;
                    
                    Console.WriteLine($"{kind}: {name} - {filePath}:{line}:{column}");
                }
            }
        }
        catch (Exception)
        {
            Console.WriteLine($"Error parsing symbol: {symbol}");
        }
    }

    public void OutputDocumentSymbol(JToken symbol, string indent = "")
    {
        try
        {
            var name = symbol["name"]?.ToString();
            var kind = symbol["kind"]?.ToString();
            var selectionRange = symbol["selectionRange"] ?? symbol["range"];
            
            if (selectionRange != null && name != null)
            {
                var line = (selectionRange["start"]?["line"]?.Value<int>() ?? 0) + 1;
                var column = (selectionRange["start"]?["character"]?.Value<int>() ?? 0) + 1;
                
                Console.WriteLine($"{indent}{kind}: {name} - Line {line}:{column}");
                
                var children = symbol["children"];
                if (children is JArray childArray)
                {
                    foreach (var child in childArray)
                    {
                        OutputDocumentSymbol(child, indent + "  ");
                    }
                }
            }
        }
        catch (Exception)
        {
            Console.WriteLine($"Error parsing document symbol: {symbol}");
        }
    }
}

public class LspSettings : CommandSettings
{
    [CommandArgument(0, "<FILE>")]
    [Description("The source file to analyze")]
    public string FilePath { get; init; } = string.Empty;

    [CommandOption("-l|--line")]
    [Description("Line number (0-based) for position")]
    [DefaultValue(0)]
    public int LineNumber { get; init; } = 0;

    [CommandOption("-c|--column")]
    [Description("Column number (0-based) for position")]
    [DefaultValue(0)]
    public int ColumnNumber { get; init; } = 0;

    [CommandOption("-s|--server")]
    [Description("LSP server executable path")]
    public string? ServerPath { get; init; }

    [CommandOption("--server-args")]
    [Description("Arguments to pass to the LSP server")]
    public string? ServerArgs { get; init; }

    [CommandOption("-q|--query")]
    [Description("Query string for search operations")]
    public string? Query { get; init; }
}