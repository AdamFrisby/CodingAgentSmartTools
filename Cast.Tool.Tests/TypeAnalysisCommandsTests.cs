using System.IO;
using System.Threading.Tasks;
using Xunit;
using Cast.Tool.Commands;

namespace Cast.Tool.Tests;

public class TypeAnalysisCommandsTests
{
    private const string TestCode = @"using System;
using System.Collections.Generic;

namespace TestProject
{
    public class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }
        
        public void SayHello()
        {
            Console.WriteLine($""Hello, I'm {Name}"");
        }
    }
    
    public interface IRepository
    {
        void Save(Person person);
    }
    
    public struct Point
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}";

    [Fact]
    public async Task DescribeTypeCommand_WithValidType_ShouldReturnTypeDetails()
    {
        // Arrange
        var tempFile = await CreateTempCsFile(TestCode);
        var command = new DescribeTypeCommand();
        var settings = new DescribeTypeCommand.Settings
        {
            FilePath = tempFile,
            TypeName = "Person"
        };

        try
        {
            // Act
            var result = await command.ExecuteAsync(null!, settings);

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DescribeTypeCommand_WithInvalidType_ShouldReturnError()
    {
        // Arrange
        var tempFile = await CreateTempCsFile(TestCode);
        var command = new DescribeTypeCommand();
        var settings = new DescribeTypeCommand.Settings
        {
            FilePath = tempFile,
            TypeName = "NonExistentType"
        };

        try
        {
            // Act
            var result = await command.ExecuteAsync(null!, settings);

            // Assert
            Assert.Equal(1, result); // Error code
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WhatTypeCommand_WithValidSymbol_ShouldReturnTypeInfo()
    {
        // Arrange
        var tempFile = await CreateTempCsFile(TestCode);
        var command = new WhatTypeCommand();
        var settings = new WhatTypeCommand.WhatTypeSettings
        {
            FilePath = tempFile,
            LineNumber = 8, // Line with "Name" property
            ColumnNumber = 20,
            DescribeType = false
        };

        try
        {
            // Act
            var result = await command.ExecuteAsync(null!, settings);

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ListTypesCommand_LocalOnly_ShouldListLocalTypes()
    {
        // Arrange
        var tempFile = await CreateTempCsFile(TestCode);
        var command = new ListTypesCommand();
        var settings = new ListTypesCommand.ListTypesSettings
        {
            FilePath = tempFile,
            LocalOnly = true
        };

        try
        {
            // Act
            var result = await command.ExecuteAsync(null!, settings);

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ListTypesCommand_WithAssemblyFilter_ShouldWork()
    {
        // Arrange
        var tempFile = await CreateTempCsFile(TestCode);
        var command = new ListTypesCommand();
        var settings = new ListTypesCommand.ListTypesSettings
        {
            FilePath = tempFile,
            AssemblyFilter = "Current",
            LocalOnly = false
        };

        try
        {
            // Act
            var result = await command.ExecuteAsync(null!, settings);

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private async Task<string> CreateTempCsFile(string content)
    {
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, content);
        return csFile;
    }
}