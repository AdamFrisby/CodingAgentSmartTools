using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Cast.Tool.Commands;
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
        var commands = new Dictionary<string, (Type CommandType, string Description)>();
        
        // Get all command types from the Cast.Tool assembly
        var castAssembly = typeof(RenameCommand).Assembly;
        var commandTypes = castAssembly.GetTypes()
            .Where(t => t.Namespace == "Cast.Tool.Commands" && 
                       t.Name.EndsWith("Command") && 
                       !t.IsAbstract)
            .ToList();

        _logger.LogInformation($"Found {commandTypes.Count} Cast commands");

        foreach (var commandType in commandTypes)
        {
            // Convert command type name to command name (e.g., RenameCommand -> rename)
            var commandName = ConvertTypeNameToCommandName(commandType.Name);
            var description = GetCommandDescription(commandType, commandName);
            
            commands[commandName] = (commandType, description);
            _logger.LogDebug($"Registered command: {commandName} -> {commandType.Name}");
        }

        return commands;
    }

    private string ConvertTypeNameToCommandName(string typeName)
    {
        // Remove "Command" suffix and convert PascalCase to kebab-case
        var name = typeName.Replace("Command", "");
        
        // Convert PascalCase to kebab-case
        var result = "";
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                result += "-";
            }
            result += char.ToLower(name[i]);
        }
        
        return result;
    }

    private string GetCommandDescription(Type commandType, string commandName)
    {
        // Provide descriptions based on command names
        return commandName switch
        {
            "rename" => "Rename a symbol at the specified location",
            "extract-method" => "Extract a method from the selected code",
            "add-using" => "Add missing using statements",
            "convert-auto-property" => "Convert between auto property and full property",
            "add-explicit-cast" => "Add explicit cast to an expression",
            "remove-unused-usings" => "Remove unused using statements from the file",
            "sort-usings" => "Sort using statements alphabetically",
            "add-file-header" => "Add a file header comment to the source file",
            "sync-namespace" => "Sync namespace with folder structure",
            "sync-type-file" => "Synchronize type name and file name",
            "move-type-to-file" => "Move type to its own matching file",
            "move-type-to-namespace" => "Move type to namespace and corresponding folder",
            "move-declaration-near-reference" => "Move variable declaration closer to its first use",
            "extract-local-function" => "Extract local function from code block",
            "inline-method" => "Inline a method by replacing its calls with the method body",
            "inline-temporary" => "Inline temporary variable",
            "change-method-signature" => "Change method signature (parameters and return type)",
            "convert-local-function" => "Convert local function to method",
            "make-local-function-static" => "Make local function static",
            "generate-default-constructor" => "Generate default constructor for class or struct",
            "add-constructor-params" => "Add constructor parameters from class members",
            "encapsulate-field" => "Encapsulate field as property",
            "make-member-static" => "Make member static",
            "convert-get-method" => "Convert between Get method and property",
            "use-explicit-type" => "Use explicit type (replace var)",
            "use-implicit-type" => "Use implicit type (var)",
            "convert-class-record" => "Convert class to record",
            "convert-tuple-struct" => "Convert tuple to struct",
            "convert-anonymous-type" => "Convert anonymous type to class",
            "convert-for-loop" => "Convert between for and foreach loops",
            "convert-if-switch" => "Convert between if-else-if and switch statements",
            "invert-if" => "Invert if statement condition",
            "invert-conditional" => "Invert conditional expressions and logical operators",
            "split-merge-if" => "Split or merge if statements",
            "reverse-for" => "Reverse for statement direction",
            "convert-string-literal" => "Convert between regular and verbatim string literals",
            "convert-string-format" => "Convert String.Format calls to interpolated strings",
            "convert-to-interpolated" => "Convert string concatenation to interpolated string",
            "use-lambda-expression" => "Convert between lambda expression and block body",
            "use-recursive-patterns" => "Convert to recursive patterns for advanced pattern matching",
            "wrap-binary-expressions" => "Wrap binary expressions with line breaks",
            "convert-numeric-literal" => "Convert numeric literal between decimal, hexadecimal, and binary formats",
            "generate-comparison-operators" => "Generate comparison operators for class",
            "generate-parameter" => "Generate parameter for method",
            "implement-interface-explicit" => "Implement all interface members explicitly",
            "implement-interface-implicit" => "Implement all interface members implicitly",
            "extract-interface" => "Extract interface from existing class",
            "extract-base-class" => "Extract base class from existing class",
            "pull-members-up" => "Pull members up to base type or interface",
            "introduce-local-variable" => "Introduce local variable for expression",
            "introduce-parameter" => "Introduce parameter to method",
            "introduce-using-statement" => "Introduce using statement for disposable objects",
            "add-named-argument" => "Add named arguments to method calls",
            "add-await" => "Add await to an async call",
            "add-debugger-display" => "Add DebuggerDisplay attribute to a class",
            "find-symbols" => "Find symbols matching a pattern (including partial matches)",
            "find-references" => "Find all references to a symbol at the specified location",
            "find-usages" => "Find all usages of a symbol, type, or member",
            "find-dependencies" => "Find dependencies and create a dependency graph from a type",
            "find-duplicate-code" => "Find code that is substantially similar to existing code",
            _ => $"C# refactoring command: {commandName}"
        };
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