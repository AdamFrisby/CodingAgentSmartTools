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
        
    config.AddCommand<AddConstructorParametersCommand>("add-constructor-params")
        .WithDescription("Add constructor parameters from class members");
        
    config.AddCommand<AddDebuggerDisplayCommand>("add-debugger-display")
        .WithDescription("Add DebuggerDisplay attribute to a class");
        
    config.AddCommand<AddFileHeaderCommand>("add-file-header")
        .WithDescription("Add a file header comment to the source file");
        
    config.AddCommand<AddNamedArgumentCommand>("add-named-argument")
        .WithDescription("Add named arguments to method calls");
        
    config.AddCommand<ConvertForLoopCommand>("convert-for-loop")
        .WithDescription("Convert between for and foreach loops");
        
    config.AddCommand<ChangeMethodSignatureCommand>("change-method-signature")
        .WithDescription("Change method signature (parameters and return type)");
        
    config.AddCommand<ConvertAnonymousTypeToClassCommand>("convert-anonymous-type")
        .WithDescription("Convert anonymous type to class");
});

return app.Run(args);
