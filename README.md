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

## Testing

To verify the tool works correctly, run the integration test scripts:

**Linux/macOS:**
```bash
./test-simple.sh
```

**Windows (PowerShell):**
```powershell
.\test-simple.ps1
```

These scripts demonstrate the Cast Tool's functionality by:
1. Building the project to an executable
2. Creating a copy of the codebase
3. Using the executable to analyze and modify the copy
4. Verifying expected changes occur
5. Confirming the modified copy still builds successfully

The integration tests showcase core functionality including adding using statements, refactoring methods to properties, sorting using statements, and analyzing code symbols.

## Usage

The `cast` tool provides command-line access to 56 C# refactoring operations using the Roslyn compiler services.

### Command Categories

#### Code Analysis & Cleanup
- **add-using** - Add missing using statements
- **remove-unused-usings** - Remove unused using statements from the file
- **sort-usings** - Sort using statements alphabetically with optional System separation
- **add-file-header** - Add a file header comment to the source file
- **sync-namespace** - Sync namespace with folder structure
- **sync-type-file** - Synchronize type name and file name

#### Symbol Refactoring
- **rename** - Rename a symbol at the specified location
- **move-type-to-file** - Move type to its own matching file
- **move-type-to-namespace** - Move type to namespace and corresponding folder
- **move-declaration-near-reference** - Move variable declaration closer to its first use

#### Method & Function Operations
- **extract-method** - Extract a method from the selected code
- **extract-local-function** - Extract local function from code block
- **inline-method** - Inline a method by replacing its calls with the method body
- **inline-temporary** - Inline temporary variable
- **change-method-signature** - Change method signature (parameters and return type)
- **convert-local-function** - Convert local function to method
- **make-local-function-static** - Make local function static
- **generate-default-constructor** - Generate default constructor for class or struct
- **add-constructor-params** - Add constructor parameters from class members

#### Property & Field Operations
- **convert-auto-property** - Convert between auto property and full property
- **encapsulate-field** - Encapsulate field as property
- **make-member-static** - Make member static
- **convert-get-method** - Convert between Get method and property

#### Type Conversions
- **add-explicit-cast** - Add explicit cast to an expression
- **convert-cast-as** - Convert between cast and as expressions
- **use-explicit-type** - Use explicit type (replace var)
- **use-implicit-type** - Use implicit type (var)
- **convert-class-record** - Convert class to record
- **convert-tuple-struct** - Convert tuple to struct
- **convert-anonymous-type** - Convert anonymous type to class

#### Control Flow & Logic
- **convert-for-loop** - Convert between for and foreach loops
- **convert-if-switch** - Convert between if-else-if and switch statements
- **invert-if** - Invert if statement condition
- **invert-conditional** - Invert conditional expressions and logical operators
- **split-merge-if** - Split or merge if statements
- **reverse-for** - Reverse for statement direction

#### String Operations
- **convert-string-literal** - Convert between regular and verbatim string literals
- **convert-string-format** - Convert String.Format calls to interpolated strings
- **convert-to-interpolated** - Convert string concatenation to interpolated string

#### Advanced Patterns & Expressions
- **use-lambda-expression** - Convert between lambda expression and block body
- **use-recursive-patterns** - Convert to recursive patterns for advanced pattern matching
- **wrap-binary-expressions** - Wrap binary expressions with line breaks
- **convert-numeric-literal** - Convert numeric literal between decimal, hexadecimal, and binary formats

#### Code Generation
- **generate-comparison-operators** - Generate comparison operators for class
- **generate-parameter** - Generate parameter for method
- **implement-interface-explicit** - Implement all interface members explicitly
- **implement-interface-implicit** - Implement all interface members implicitly
- **extract-interface** - Extract interface from existing class
- **extract-base-class** - Extract base class from existing class
- **pull-members-up** - Pull members up to base type or interface

#### Variable & Parameter Management
- **introduce-local-variable** - Introduce local variable for expression
- **introduce-parameter** - Introduce parameter to method
- **introduce-using-statement** - Introduce using statement for disposable objects
- **add-named-argument** - Add named arguments to method calls

#### Async & Debugging
- **add-await** - Add await to an async call
- **add-debugger-display** - Add DebuggerDisplay attribute to a class

#### Code Analysis Tools
- **find-symbols** - Find symbols matching a pattern (including partial matches)
- **find-references** - Find all references to a symbol at the specified location  
- **find-usages** - Find all usages of a symbol, type, or member
- **find-dependencies** - Find dependencies and create a dependency graph from a type
- **find-duplicate-code** - Find code that is substantially similar to existing code

**Analysis Output Format**: All analysis tools output results in grep-style format: `Filename:Line <copy of line>`

### Common Options

- `--line <number>`: Line number (1-based) where the refactoring should be applied
- `--column <number>`: Column number (0-based) for precise positioning
- `--output <path>`: Output file path (defaults to overwriting the input file)
- `--dry-run`: Show what changes would be made without applying them

### Examples

```bash
# Code cleanup and analysis
cast add-using Calculator.cs "System.Linq" --dry-run
cast remove-unused-usings Program.cs
cast sort-usings MyClass.cs

# Symbol refactoring
cast rename Calculator.cs "result" "sum" --line 15 --column 12
cast move-type-to-file Person.cs --output "./Models/Person.cs"

# Method operations
cast extract-method Calculator.cs "CalculateTotal" --line 10 --end-line 15
cast inline-method Helper.cs --line 8
cast change-method-signature MyClass.cs --line 20

# Property and field operations
cast convert-auto-property Person.cs --line 8 --to full
cast encapsulate-field Customer.cs --line 5

# Type conversions
cast add-explicit-cast Calculator.cs "int" --line 12 --column 20
cast use-implicit-type Program.cs --line 7
cast convert-class-record User.cs --line 3

# Control flow
cast convert-for-loop Program.cs --line 15 --to foreach
cast convert-if-switch Program.cs --line 20
cast invert-if MyMethod.cs --line 10

# String operations
cast convert-to-interpolated Logger.cs --line 12
cast convert-string-format Output.cs --line 8

# Code generation
cast implement-interface-implicit MyClass.cs --line 5
cast extract-interface Customer.cs --interface-name "ICustomer"
cast generate-default-constructor Person.cs

# Code analysis
cast find-symbols MyClass.cs --pattern "Add*"
cast find-references Calculator.cs --line 15 --column 12
cast find-usages Service.cs --line 8 --column 20
cast find-dependencies --type "Calculator" MyClass.cs
cast find-duplicate-code LargeFile.cs
```

## Implemented Commands

✅ **61 Complete Commands** - All major C# refactoring operations plus powerful analysis tools are now implemented:

**Code Analysis & Cleanup** (6 commands)  
**Symbol Refactoring** (4 commands)  
**Method & Function Operations** (9 commands)  
**Property & Field Operations** (4 commands)  
**Type Conversions** (7 commands)  
**Control Flow & Logic** (6 commands)  
**String Operations** (3 commands)  
**Advanced Patterns & Expressions** (4 commands)  
**Code Generation** (7 commands)  
**Variable & Parameter Management** (4 commands)  
**Async & Debugging** (2 commands)  
**Code Analysis Tools** (5 commands)  

The tool now provides comprehensive coverage of C# refactoring operations plus powerful analysis capabilities, making it ideal for coding agents and automated workflows that need safe, precise code transformations and deep code analysis.

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

The core refactoring functionality is now complete with 56 commands implemented. To contribute additional features or improvements:

1. **Enhancement suggestions**: Open an issue to discuss new features or command improvements
2. **Bug fixes**: Create a new command class inheriting from `Command<TSettings>`
3. **New commands**: Implement additional refactoring logic using Roslyn APIs
4. **Testing**: Register the command in `Program.cs` and add comprehensive tests in `Cast.Tool.Tests`

The established pattern makes it straightforward to add specialized refactoring operations for specific use cases or domain-specific transformations.

## License

MIT License - see LICENSE file for details.
