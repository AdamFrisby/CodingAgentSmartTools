using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Cast.Tool.Core;
using System.Reflection;
using System.Text.Json;

namespace Cast.Tool.McpServer;

public class CastMcpServer
{
    private readonly ILogger<CastMcpServer> _logger;
    private readonly Dictionary<string, (Type CommandType, string Description)> _commands;

    public CastMcpServer(ILogger<CastMcpServer> logger)
    {
        _logger = logger;
        _commands = DiscoverCastCommands();
    }

    private Dictionary<string, (Type CommandType, string Description)> DiscoverCastCommands()
    {
        _logger.LogInformation($"Found {CastCommandRegistry.Commands.Count} Cast commands");

        foreach (var (commandName, (commandType, description)) in CastCommandRegistry.Commands)
        {
            _logger.LogDebug($"Registered command: {commandName} -> {commandType.Name}");
        }

        return new Dictionary<string, (Type CommandType, string Description)>(CastCommandRegistry.Commands);
    }

    public async Task<ListToolsResult> HandleListToolsAsync(RequestContext<ListToolsRequestParams> context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling list tools request");
        
        var tools = _commands.Select(kvp => new ModelContextProtocol.Protocol.Tool
        {
            Name = $"cast_{kvp.Key.Replace("-", "_")}",
            Description = kvp.Value.Description,
            InputSchema = JsonSerializer.SerializeToElement(CreateToolInputSchema(kvp.Key, kvp.Value.CommandType))
        }).ToList();

        return new ListToolsResult { Tools = tools };
    }

    public async Task<CallToolResult> HandleCallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Handling call tool request for: {context.Params.Name}");
        
        try
        {
            // Extract command name from tool name (remove "cast_" prefix and convert back)
            var commandName = context.Params.Name?.StartsWith("cast_") == true 
                ? context.Params.Name.Substring(5).Replace("_", "-")
                : context.Params.Name ?? "unknown";

            if (!_commands.TryGetValue(commandName, out var commandInfo))
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Unknown command: {commandName}" }],
                    IsError = true
                };
            }

            // Execute the Cast command
            var result = await ExecuteCastCommandAsync(commandName, commandInfo.CommandType, context.Params.Arguments);
            
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result }],
                IsError = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Cast command");
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Error: {ex.Message}" }],
                IsError = true
            };
        }
    }

    private object CreateToolInputSchema(string commandName, Type commandType)
    {
        // Create a JSON schema for the command's input parameters
        var schema = new
        {
            type = "object",
            description = $"Input parameters for {commandName} command",
            properties = new Dictionary<string, object>(),
            required = new List<string> { "file_path" }
        };

        // Add common parameters that most commands need
        schema.properties["file_path"] = new
        {
            type = "string",
            description = "The C# source file to refactor"
        };

        schema.properties["line_number"] = new
        {
            type = "integer",
            description = "Line number (1-based) where the refactoring should be applied",
            minimum = 1,
            @default = 1
        };

        schema.properties["column_number"] = new
        {
            type = "integer",
            description = "Column number (0-based) where the refactoring should be applied",
            minimum = 0,
            @default = 0
        };

        schema.properties["output_path"] = new
        {
            type = "string",
            description = "Output file path (optional, defaults to overwriting the input file)"
        };

        schema.properties["dry_run"] = new
        {
            type = "boolean",
            description = "Show what changes would be made without applying them",
            @default = false
        };

        // Add command-specific parameters based on command name
        AddCommandSpecificParameters(schema, commandName);

        return schema;
    }

    private void AddCommandSpecificParameters(dynamic schema, string commandName)
    {
        switch (commandName)
        {
            case "rename":
                schema.properties["old_name"] = new
                {
                    type = "string",
                    description = "Current name of the symbol to rename"
                };
                schema.properties["new_name"] = new
                {
                    type = "string", 
                    description = "New name for the symbol"
                };
                schema.required = new[] { "file_path", "old_name", "new_name" };
                break;
                
            case "extract-method":
                schema.properties["method_name"] = new
                {
                    type = "string",
                    description = "Name for the extracted method"
                };
                schema.properties["end_line_number"] = new
                {
                    type = "integer",
                    description = "End line number for the code selection to extract"
                };
                schema.required = new[] { "file_path", "method_name" };
                break;
                
            case "add-using":
                schema.properties["namespace"] = new
                {
                    type = "string",
                    description = "Namespace to add as a using statement"
                };
                schema.required = new[] { "file_path", "namespace" };
                break;
                
            case "add-explicit-cast":
                schema.properties["cast_type"] = new
                {
                    type = "string",
                    description = "Type to cast to"
                };
                schema.required = new[] { "file_path", "cast_type" };
                break;
                
            case "add-file-header":
                schema.properties["header_text"] = new
                {
                    type = "string",
                    description = "Header text to add to the file"
                };
                break;
        }
    }

    private async Task<string> ExecuteCastCommandAsync(string commandName, Type commandType, IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        // For now, simulate command execution
        // In a full implementation, we would properly invoke the command with parsed arguments
        var filePath = GetArgumentValue(arguments, "file_path");
        var dryRun = GetArgumentValue(arguments, "dry_run", false);
        
        if (string.IsNullOrEmpty(filePath))
        {
            return "Error: file_path is required";
        }

        if (!File.Exists(filePath))
        {
            return $"Error: File not found: {filePath}";
        }

        // For the initial implementation, return a success message
        // This would be replaced with actual command execution using Spectre.Console.Cli
        var result = $"Successfully executed {commandName} on {filePath}";
        if (dryRun)
        {
            result = $"[DRY RUN] Would execute {commandName} on {filePath}";
        }

        _logger.LogInformation($"Executed command {commandName}: {result}");
        return result;
    }

    private string GetArgumentValue(IReadOnlyDictionary<string, JsonElement>? arguments, string key, string defaultValue = "")
    {
        if (arguments == null || !arguments.TryGetValue(key, out var property))
            return defaultValue;
            
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? defaultValue : defaultValue;
    }

    private bool GetArgumentValue(IReadOnlyDictionary<string, JsonElement>? arguments, string key, bool defaultValue)
    {
        if (arguments == null || !arguments.TryGetValue(key, out var property))
            return defaultValue;
            
        return property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False 
            ? property.GetBoolean() 
            : defaultValue;
    }

    private int GetArgumentValue(IReadOnlyDictionary<string, JsonElement>? arguments, string key, int defaultValue)
    {
        if (arguments == null || !arguments.TryGetValue(key, out var property))
            return defaultValue;
            
        return property.ValueKind == JsonValueKind.Number ? property.GetInt32() : defaultValue;
    }
}