# LSP Server Support

This document describes the LSP (Language Server Protocol) commands available in Cast.Tool. These commands allow sophisticated code analysis by interfacing with existing LSP servers.

## Available LSP Commands

### lsp-goto-definition
Go to definition using an LSP server.

**Usage:**
```bash
cast lsp-goto-definition <file> --line <line> --column <column>
```

**Options:**
- `--line`, `-l`: Line number (0-based) for position
- `--column`, `-c`: Column number (0-based) for position
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-goto-definition MyClass.cs --line 10 --column 5
```

### lsp-find-references
Find references using an LSP server.

**Usage:**
```bash
cast lsp-find-references <file> --line <line> --column <column>
```

**Options:**
- `--line`, `-l`: Line number (0-based) for position
- `--column`, `-c`: Column number (0-based) for position
- `--include-declaration`: Include the declaration in the results (default: true)
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-find-references MyClass.cs --line 10 --column 5 --include-declaration
```

### lsp-hover
Get hover information using an LSP server.

**Usage:**
```bash
cast lsp-hover <file> --line <line> --column <column>
```

**Options:**
- `--line`, `-l`: Line number (0-based) for position
- `--column`, `-c`: Column number (0-based) for position
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-hover MyClass.cs --line 10 --column 5
```

### lsp-workspace-symbols
Search workspace symbols using an LSP server.

**Usage:**
```bash
cast lsp-workspace-symbols <file> [query]
```

**Options:**
- `query`: Search query for workspace symbols
- `--query`, `-q`: Alternative way to specify search query
- `--workspace`, `-w`: Workspace root directory
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-workspace-symbols MyClass.cs "MyClass"
```

### lsp-document-symbols
Get document symbols using an LSP server.

**Usage:**
```bash
cast lsp-document-symbols <file>
```

**Options:**
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-document-symbols MyClass.cs
```

### lsp-code-actions
Get available code actions using an LSP server.

**Usage:**
```bash
cast lsp-code-actions <file> --line <line> --column <column>
```

**Options:**
- `--line`, `-l`: Line number (0-based) for position
- `--column`, `-c`: Column number (0-based) for position
- `--end-line`: End line number (0-based) for range selection
- `--end-column`: End column number (0-based) for range selection
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-code-actions MyClass.cs --line 10 --column 5 --end-line 10 --end-column 20
```

### lsp-format-document
Format document using an LSP server.

**Usage:**
```bash
cast lsp-format-document <file>
```

**Options:**
- `--tab-size`: Tab size for formatting (default: 4)
- `--insert-spaces`: Use spaces instead of tabs (default: true)
- `--output`: Output formatted content to a file (default: overwrite original)
- `--dry-run`: Show formatting changes without applying them
- `--server`, `-s`: LSP server executable path
- `--server-args`: Arguments to pass to the LSP server

**Example:**
```bash
cast lsp-format-document MyClass.cs --dry-run
cast lsp-format-document MyClass.cs --output FormattedClass.cs
```

## Supported Language Servers

Cast.Tool will automatically detect and use appropriate LSP servers for different file types:

- **C#**: `csharp-ls`, `omnisharp`
- **TypeScript/JavaScript**: `typescript-language-server`
- **Python**: `pylsp`, `pyright`
- **Java**: `jdtls`
- **C/C++**: `clangd`
- **Go**: `gopls`
- **Rust**: `rust-analyzer`
- **Ruby**: `solargraph`
- **PHP**: `intelephense`

## Manual Server Configuration

You can specify a custom LSP server using the `--server` option:

```bash
cast lsp-goto-definition MyClass.cs --line 10 --column 5 --server /path/to/my-language-server --server-args "--arg1 value1 --arg2"
```

## Installation Notes

1. Make sure the appropriate LSP server is installed and available in your PATH
2. For C# development, install OmniSharp or the C# Language Server
3. For TypeScript/JavaScript, install typescript-language-server: `npm install -g typescript-language-server`
4. For Python, install python-lsp-server: `pip install python-lsp-server`

## Error Handling

- If no LSP server is found, Cast.Tool will display an error message
- If the LSP server fails to start, an error will be shown
- Network timeouts and communication errors are handled gracefully
- Malformed responses from LSP servers will be logged but won't crash the application