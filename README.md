# Coding Agent Smart Tools `cast`
New tools for baking into containers which improve agentic coding behaviours, currently work for C#/.NET based projects which support the Roslyn analyser.

## Why?
Well, apparently all the LLMs out there think `sed` is the height of programmer efficiency - modern IDEs have modern refactoring tools; this project brings them to your LLM.

This should make refactoring code via coding agents much, much safer - and faster too.

## Installation

Build the tool from source:

```bash
git clone https://github.com/AdamFrisby/CodingAgentSmartTools.git
cd CodingAgentSmartTools
dotnet build
```

## Usage

The `cast` tool provides command-line access to C# refactoring operations using the Roslyn compiler services.

### Available Commands

#### Add Using Statement
Add a missing using statement to a C# file:

```bash
dotnet run --project Cast.Tool -- add-using MyFile.cs "System.Collections.Generic" [--dry-run]
```

#### Rename Symbol
Rename a symbol (variable, method, class, etc.) at a specific location:

```bash
dotnet run --project Cast.Tool -- rename MyFile.cs "oldName" "newName" --line 10 [--column 5] [--dry-run]
```

#### Extract Method
Extract selected code into a new method:

```bash
dotnet run --project Cast.Tool -- extract-method MyFile.cs "NewMethodName" --line 10 [--end-line 15] [--dry-run]
```

#### Convert Auto Property
Convert between auto properties and full properties:

```bash
dotnet run --project Cast.Tool -- convert-auto-property MyFile.cs --line 8 [--to full|auto] [--dry-run]
```

#### Add Explicit Cast
Add an explicit cast to an expression:

```bash
dotnet run --project Cast.Tool -- add-explicit-cast MyFile.cs "int" --line 12 [--column 20] [--dry-run]
```

### Common Options

- `--line <number>`: Line number (1-based) where the refactoring should be applied
- `--column <number>`: Column number (0-based) for precise positioning
- `--output <path>`: Output file path (defaults to overwriting the input file)
- `--dry-run`: Show what changes would be made without applying them

### Examples

```bash
# Add a using statement
cast add-using Calculator.cs "System.Linq" --dry-run

# Rename a variable
cast rename Calculator.cs "result" "sum" --line 15 --column 12

# Extract method from lines 10-15
cast extract-method Calculator.cs "CalculateTotal" --line 10 --end-line 15

# Convert auto property to full property
cast convert-auto-property Person.cs --line 8 --to full
```

## Implemented Commands

✅ **Add using statement** - Add missing using/import statements  
✅ **Rename symbol** - Rename variables, methods, classes, etc.  
✅ **Extract method** - Extract code into a new method  
✅ **Convert auto property** - Convert between auto and full properties  
✅ **Add explicit cast** - Add explicit type casts  

## Future Commands

The tool is designed to support all major C# refactoring operations. Future implementations will include:

- **Change method signature** - Reorder/add/remove parameters
- **Convert for loop ⇄ foreach** - Convert between loop types
- **Extract interface** - Extract interface from class
- **Inline method** - Inline method calls
- **Move type to namespace** - Reorganize code structure
- And many more...

## Architecture

The tool is built using:
- **Roslyn** for C# code analysis and transformation
- **Spectre.Console.Cli** for command-line interface
- **xUnit** for testing

Each refactoring command follows a consistent pattern:
1. Parse and validate input arguments
2. Load and analyze the C# source file using Roslyn
3. Apply the requested transformation
4. Output the modified code

## Contributing

To add a new refactoring command:

1. Create a new command class inheriting from `Command<TSettings>`
2. Implement the refactoring logic using Roslyn APIs
3. Register the command in `Program.cs`
4. Add tests in the `Cast.Tool.Tests` project

## License

MIT License - see LICENSE file for details.
