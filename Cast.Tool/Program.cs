using Spectre.Console.Cli;
using Cast.Tool.Core;
using System.Reflection;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("cast");
    
    // Register all commands from the registry
    foreach (var (commandName, (commandType, description)) in CastCommandRegistry.Commands)
    {
        // Use reflection to call the generic AddCommand method
        var addCommandMethod = typeof(IConfigurator).GetMethod("AddCommand", 1, new[] { typeof(string) })!
            .MakeGenericMethod(commandType);
        
        var commandConfig = addCommandMethod.Invoke(config, new object[] { commandName });
        
        // Call WithDescription on the returned command configuration
        var withDescriptionMethod = commandConfig!.GetType().GetMethod("WithDescription")!;
        withDescriptionMethod.Invoke(commandConfig, new object[] { description });
    }
});

return app.Run(args);
