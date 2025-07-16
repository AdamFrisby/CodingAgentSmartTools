# Cast Tool MCP Server

The Cast Tool MCP (Model Context Protocol) Server exposes all 61+ C# refactoring commands from the Cast tool as MCP tools that can be used by AI agents and other clients.

## Features

- **61+ C# Refactoring Tools**: All Cast refactoring commands are available as MCP tools
- **JSON Schema Validation**: Each tool has proper input schema definitions
- **Error Handling**: Comprehensive error handling and validation
- **Logging**: Built-in logging for monitoring and debugging

## Available Tools

All Cast commands are exposed with the prefix `cast_` and kebab-case names converted to underscore. For example:

- `cast_rename` - Rename a symbol at the specified location
- `cast_extract_method` - Extract a method from the selected code
- `cast_add_using` - Add missing using statements
- `cast_convert_auto_property` - Convert between auto property and full property
- `cast_add_explicit_cast` - Add explicit cast to an expression
- `cast_remove_unused_usings` - Remove unused using statements
- `cast_sort_usings` - Sort using statements alphabetically
- ... and many more

### Tool Categories

1. **Code Analysis & Cleanup** (6 commands)
2. **Symbol Refactoring** (4 commands)  
3. **Method & Function Operations** (9 commands)
4. **Property & Field Operations** (4 commands)
5. **Type Conversions** (7 commands)
6. **Control Flow & Logic** (6 commands)
7. **String Operations** (3 commands)
8. **Advanced Patterns & Expressions** (4 commands)
9. **Code Generation** (7 commands)
10. **Variable & Parameter Management** (4 commands)
11. **Async & Debugging** (2 commands)
12. **Code Analysis Tools** (5 commands)

## Usage

### Running the MCP Server

```bash
# Navigate to the project directory
cd CodingAgentSmartTools

# Run the MCP server
dotnet run --project Cast.Tool.McpServer/Cast.Tool.McpServer.csproj
```

The server will start and listen for MCP protocol requests on stdin/stdout.

### Tool Parameters

All tools accept these common parameters:

- `file_path` (required): The C# source file to refactor
- `line_number` (optional): Line number (1-based) where the refactoring should be applied (default: 1)
- `column_number` (optional): Column number (0-based) where the refactoring should be applied (default: 0)
- `output_path` (optional): Output file path (defaults to overwriting the input file)
- `dry_run` (optional): Show what changes would be made without applying them (default: false)

Some tools have additional specific parameters:

#### rename
- `old_name` (required): Current name of the symbol to rename
- `new_name` (required): New name for the symbol

#### extract_method
- `method_name` (required): Name for the extracted method
- `end_line_number` (optional): End line number for the code selection to extract

#### add_using
- `namespace` (required): Namespace to add as a using statement

#### add_explicit_cast
- `cast_type` (required): Type to cast to

### Example MCP Tool Call

```json
{
  "name": "cast_rename",
  "arguments": {
    "file_path": "/path/to/MyClass.cs",
    "line_number": 15,
    "column_number": 12,
    "old_name": "oldVariableName",
    "new_name": "newVariableName",
    "dry_run": true
  }
}
```

## Development

### Building

```bash
dotnet build Cast.Tool.McpServer/Cast.Tool.McpServer.csproj
```

### Testing

The MCP server project uses the existing Cast.Tool test suite to ensure compatibility:

```bash
dotnet test
```

All 73 tests should pass.

### Architecture

The MCP server consists of:

1. **CastMcpServer**: Main server class that handles MCP protocol requests
2. **Command Discovery**: Automatically discovers all Cast commands via reflection
3. **Schema Generation**: Creates JSON schemas for each command's parameters
4. **Request Handling**: Processes `list_tools` and `call_tool` MCP requests

### Dependencies

- ModelContextProtocol (0.3.0-preview.2)
- Microsoft.Extensions.Hosting (9.0.7)
- Cast.Tool (project reference)

## Integration

To integrate this MCP server with an AI agent or client:

1. Start the MCP server process
2. Communicate via stdin/stdout using the MCP protocol
3. Use `list_tools` to discover available refactoring commands
4. Use `call_tool` to execute specific refactoring operations

The server exposes all Cast functionality through a standardized MCP interface, making it easy for AI agents to perform sophisticated C# code refactoring operations safely and efficiently.