using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cast.Tool.Core;

public class LspClient : IDisposable
{
    private JsonRpc? _jsonRpc;
    private Process? _serverProcess;
    private readonly string _serverExecutable;
    private readonly string[] _serverArgs;
    private bool _disposed;
    private int _requestId = 1;

    public LspClient(string serverExecutable, params string[] serverArgs)
    {
        _serverExecutable = serverExecutable;
        _serverArgs = serverArgs;
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Start the LSP server process
            _serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _serverExecutable,
                    Arguments = string.Join(" ", _serverArgs),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!_serverProcess.Start())
            {
                return false;
            }

            // Initialize JsonRpc
            _jsonRpc = JsonRpc.Attach(_serverProcess.StandardInput.BaseStream, _serverProcess.StandardOutput.BaseStream);

            // Initialize the LSP connection
            var initializeParams = new
            {
                processId = Environment.ProcessId,
                capabilities = new
                {
                    textDocument = new
                    {
                        definition = new { dynamicRegistration = false },
                        references = new { dynamicRegistration = false },
                        hover = new { dynamicRegistration = false },
                        documentSymbol = new { dynamicRegistration = false },
                        codeAction = new { dynamicRegistration = false },
                        formatting = new { dynamicRegistration = false }
                    },
                    workspace = new
                    {
                        symbol = new { dynamicRegistration = false }
                    }
                }
            };

            var result = await _jsonRpc.InvokeAsync<object>("initialize", initializeParams, cancellationToken);
            await _jsonRpc.NotifyAsync("initialized", new { }, cancellationToken);

            return true;
        }
        catch (Exception)
        {
            Dispose();
            return false;
        }
    }

    public async Task<object?> GoToDefinitionAsync(string documentUri, LspPosition position, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new
            {
                textDocument = new { uri = documentUri },
                position = new { line = position.Line, character = position.Character }
            };

            var result = await _jsonRpc.InvokeAsync<object>("textDocument/definition", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<object?> FindReferencesAsync(string documentUri, LspPosition position, bool includeDeclaration = true, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new
            {
                textDocument = new { uri = documentUri },
                position = new { line = position.Line, character = position.Character },
                context = new { includeDeclaration = includeDeclaration }
            };

            var result = await _jsonRpc.InvokeAsync<object>("textDocument/references", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<object?> GetHoverInfoAsync(string documentUri, LspPosition position, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new
            {
                textDocument = new { uri = documentUri },
                position = new { line = position.Line, character = position.Character }
            };

            var result = await _jsonRpc.InvokeAsync<object>("textDocument/hover", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<object?> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new { query = query };
            var result = await _jsonRpc.InvokeAsync<object>("workspace/symbol", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<object?> GetDocumentSymbolsAsync(string documentUri, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new { textDocument = new { uri = documentUri } };
            var result = await _jsonRpc.InvokeAsync<object>("textDocument/documentSymbol", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<object?> GetCodeActionsAsync(string documentUri, LspRange range, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new
            {
                textDocument = new { uri = documentUri },
                range = new
                {
                    start = new { line = range.Start.Line, character = range.Start.Character },
                    end = new { line = range.End.Line, character = range.End.Character }
                },
                context = new { diagnostics = new object[0] }
            };

            var result = await _jsonRpc.InvokeAsync<object>("textDocument/codeAction", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<object?> FormatDocumentAsync(string documentUri, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return null;

        try
        {
            var @params = new
            {
                textDocument = new { uri = documentUri },
                options = new
                {
                    tabSize = 4,
                    insertSpaces = true
                }
            };

            var result = await _jsonRpc.InvokeAsync<object>("textDocument/formatting", @params, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task DidOpenAsync(string documentUri, string languageId, string text, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return;

        try
        {
            var @params = new
            {
                textDocument = new
                {
                    uri = documentUri,
                    languageId = languageId,
                    version = 1,
                    text = text
                }
            };

            await _jsonRpc.NotifyAsync("textDocument/didOpen", @params, cancellationToken);
        }
        catch (Exception)
        {
            // Ignore errors
        }
    }

    public async Task DidCloseAsync(string documentUri, CancellationToken cancellationToken = default)
    {
        if (_jsonRpc == null) return;

        try
        {
            var @params = new { textDocument = new { uri = documentUri } };
            await _jsonRpc.NotifyAsync("textDocument/didClose", @params, cancellationToken);
        }
        catch (Exception)
        {
            // Ignore errors
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _jsonRpc?.Dispose();
            _serverProcess?.Kill();
            _serverProcess?.Dispose();
        }
        catch (Exception)
        {
            // Ignore disposal errors
        }

        _disposed = true;
    }
}

public class LspPosition
{
    public int Line { get; set; }
    public int Character { get; set; }

    public LspPosition(int line, int character)
    {
        Line = line;
        Character = character;
    }
}

public class LspRange
{
    public LspPosition Start { get; set; }
    public LspPosition End { get; set; }

    public LspRange(LspPosition start, LspPosition end)
    {
        Start = start;
        End = end;
    }
}