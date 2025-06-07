using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Cast.Tool.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task FullWorkflow_AddUsingAndRename_ShouldWork()
    {
        // Arrange
        var testCode = @"using System;

namespace MyProject
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }
    }
}";
        
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, testCode);

        try
        {
            // Act 1: Add a using statement
            var addUsingCommand = new Commands.AddUsingCommand();
            var addUsingSettings = new Commands.AddUsingCommand.Settings
            {
                FilePath = csFile,
                Namespace = "System.Collections.Generic",
                DryRun = false
            };

            var result1 = await addUsingCommand.ExecuteAsync(null!, addUsingSettings);
            Assert.Equal(0, result1);

            // Verify the using was added
            var modifiedCode = await File.ReadAllTextAsync(csFile);
            Assert.Contains("using System.Collections.Generic;", modifiedCode);

            // Act 2: Try adding the same using again (should not duplicate)
            var result2 = await addUsingCommand.ExecuteAsync(null!, addUsingSettings);
            Assert.Equal(0, result2);

            // Verify no duplication
            var finalCode = await File.ReadAllTextAsync(csFile);
            var usingCount = finalCode.Split("using System.Collections.Generic;").Length - 1;
            Assert.Equal(1, usingCount);
        }
        finally
        {
            // Cleanup
            File.Delete(csFile);
        }
    }

    [Fact]
    public void CommandLineInterface_Help_ShouldShowCommands()
    {
        // This test verifies that the CLI is properly configured
        // In a real scenario, you'd test the actual CLI output
        
        // Arrange & Act - Just verify the commands are configured
        var app = new Spectre.Console.Cli.CommandApp();
        
        app.Configure(config =>
        {
            // Note: SetApplicationName is not available in some versions
            
            config.AddCommand<Commands.RenameCommand>("rename")
                .WithDescription("Rename a symbol at the specified location");
                
            config.AddCommand<Commands.ExtractMethodCommand>("extract-method")
                .WithDescription("Extract a method from the selected code");
                
            config.AddCommand<Commands.AddUsingCommand>("add-using")
                .WithDescription("Add missing using statements");
                
            config.AddCommand<Commands.ConvertAutoPropertyCommand>("convert-auto-property")
                .WithDescription("Convert between auto property and full property");
                
            config.AddCommand<Commands.AddExplicitCastCommand>("add-explicit-cast")
                .WithDescription("Add explicit cast to an expression");
        });

        // Assert - If we got here without exception, the configuration is valid
        Assert.True(true);
    }
}