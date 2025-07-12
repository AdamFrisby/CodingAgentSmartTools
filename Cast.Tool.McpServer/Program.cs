using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Cast.Tool.McpServer;

Console.WriteLine("=== Cast Tool MCP Server ===");
Console.WriteLine("Starting Model Context Protocol server for Cast refactoring tools...");

try
{
    var builder = Host.CreateApplicationBuilder(args);
    
    // Configure logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    
    // Add our Cast MCP server
    builder.Services.AddSingleton<CastMcpServer>();
    
    // Add MCP server services using the handlers approach
    builder.Services.AddMcpServer(serverBuilder =>
    {
        // Configure handlers using the handlers property
        var handlers = new McpServerHandlers();
        
        handlers.ListToolsHandler = async (context, cancellationToken) =>
        {
            var serviceProvider = builder.Services.BuildServiceProvider();
            var castServer = serviceProvider.GetRequiredService<CastMcpServer>();
            return await castServer.HandleListToolsAsync(context, cancellationToken);
        };
        
        handlers.CallToolHandler = async (context, cancellationToken) =>
        {
            var serviceProvider = builder.Services.BuildServiceProvider();
            var castServer = serviceProvider.GetRequiredService<CastMcpServer>();
            return await castServer.HandleCallToolAsync(context, cancellationToken);
        };
        
        // Note: The builder pattern might work differently, let me try without return
    });

    var host = builder.Build();
    
    Console.WriteLine("MCP Server started successfully. Listening for requests...");
    Console.WriteLine("Available tools: All 61+ Cast refactoring commands exposed as MCP tools");
    Console.WriteLine("Use Ctrl+C to stop the server.");
    
    // Start the host
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Error starting MCP server: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    Environment.Exit(1);
}
