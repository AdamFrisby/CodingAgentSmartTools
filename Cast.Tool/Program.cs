using Spectre.Console.Cli;
using Cast.Tool.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("cast");
    
    // Add refactoring commands
    config.AddCommand<RenameCommand>("rename")
        .WithDescription("Rename a symbol at the specified location");
        
    config.AddCommand<ExtractMethodCommand>("extract-method")
        .WithDescription("Extract a method from the selected code");
        
    config.AddCommand<AddUsingCommand>("add-using")
        .WithDescription("Add missing using statements");
        
    config.AddCommand<ConvertAutoPropertyCommand>("convert-auto-property")
        .WithDescription("Convert between auto property and full property");
        
    config.AddCommand<AddExplicitCastCommand>("add-explicit-cast")
        .WithDescription("Add explicit cast to an expression");
        
    config.AddCommand<AddAwaitCommand>("add-await")
        .WithDescription("Add await to an async call");
});

return app.Run(args);
