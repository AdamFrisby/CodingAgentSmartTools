using System.IO;
using System.Threading.Tasks;
using Xunit;
using Cast.Tool.Commands;

namespace Cast.Tool.Tests;

public class ParametersToParameterObjectCommandTests
{
    [Fact]
    public async Task ParametersToParameterObject_Class_ShouldWork()
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
            // Act
            var command = new ParametersToParameterObjectCommand();
            var settings = new ParametersToParameterObjectCommand.Settings
            {
                FilePath = csFile,
                LineNumber = 7, // Line with public int Add
                ParameterObjectName = "AddParams",
                ParameterObjectType = "class",
                DryRun = false
            };

            var result = await command.ExecuteAsync(null!, settings);
            Assert.Equal(0, result);

            // Verify the transformation
            var modifiedCode = await File.ReadAllTextAsync(csFile);
            Assert.Contains("public int Add(AddParams args)", modifiedCode);
            Assert.Contains("return args.a + args.b;", modifiedCode);
            Assert.Contains("public class AddParams", modifiedCode);
            Assert.Contains("public int a { get; set; }", modifiedCode);
            Assert.Contains("public int b { get; set; }", modifiedCode);
            Assert.Contains("public AddParams(int a, int b)", modifiedCode);
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public async Task ParametersToParameterObject_Struct_ShouldWork()
    {
        // Arrange
        var testCode = @"using System;

namespace MyProject
{
    public class Calculator
    {
        public int Multiply(int x, int y)
        {
            return x * y;
        }
    }
}";
        
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, testCode);

        try
        {
            // Act
            var command = new ParametersToParameterObjectCommand();
            var settings = new ParametersToParameterObjectCommand.Settings
            {
                FilePath = csFile,
                LineNumber = 7, // Line with public int Multiply
                ParameterObjectName = "MultiplyData",
                ParameterObjectType = "struct",
                DryRun = false
            };

            var result = await command.ExecuteAsync(null!, settings);
            Assert.Equal(0, result);

            // Verify the transformation
            var modifiedCode = await File.ReadAllTextAsync(csFile);
            Assert.Contains("public int Multiply(MultiplyData args)", modifiedCode);
            Assert.Contains("return args.x * args.y;", modifiedCode);
            Assert.Contains("public struct MultiplyData", modifiedCode);
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public async Task ParametersToParameterObject_Record_ShouldWork()
    {
        // Arrange
        var testCode = @"using System;

namespace MyProject
{
    public class Calculator
    {
        public double Divide(double numerator, double denominator)
        {
            return numerator / denominator;
        }
    }
}";
        
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, testCode);

        try
        {
            // Act
            var command = new ParametersToParameterObjectCommand();
            var settings = new ParametersToParameterObjectCommand.Settings
            {
                FilePath = csFile,
                LineNumber = 7, // Line with public double Divide
                ParameterObjectType = "record",
                DryRun = false
            };

            var result = await command.ExecuteAsync(null!, settings);
            Assert.Equal(0, result);

            // Verify the transformation
            var modifiedCode = await File.ReadAllTextAsync(csFile);
            Assert.Contains("public double Divide(DivideArgs args)", modifiedCode);
            Assert.Contains("return args.numerator / args.denominator;", modifiedCode);
            Assert.Contains("public record DivideArgs(double numerator, double denominator);", modifiedCode);
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public async Task ParametersToParameterObject_NoParameters_ShouldReturnWarning()
    {
        // Arrange
        var testCode = @"using System;

namespace MyProject
{
    public class Calculator
    {
        public int GetZero()
        {
            return 0;
        }
    }
}";
        
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, testCode);

        try
        {
            // Act
            var command = new ParametersToParameterObjectCommand();
            var settings = new ParametersToParameterObjectCommand.Settings
            {
                FilePath = csFile,
                LineNumber = 7, // Line with public int GetZero
                DryRun = false
            };

            var result = await command.ExecuteAsync(null!, settings);
            Assert.Equal(1, result); // Should return error code for no parameters
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public async Task ParametersToParameterObject_ComplexParameters_ShouldWork()
    {
        // Arrange
        var testCode = @"using System;
using System.Collections.Generic;

namespace MyProject
{
    public class Service
    {
        public void ProcessData(bool isActive, string message, List<int> numbers)
        {
            if (isActive)
            {
                Console.WriteLine(message);
                foreach (var num in numbers)
                {
                    Console.WriteLine(num);
                }
            }
        }
    }
}";
        
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, testCode);

        try
        {
            // Act
            var command = new ParametersToParameterObjectCommand();
            var settings = new ParametersToParameterObjectCommand.Settings
            {
                FilePath = csFile,
                LineNumber = 8, // Line with public void ProcessData
                ParameterObjectName = "ProcessDataRequest",
                ParameterObjectType = "class",
                DryRun = false
            };

            var result = await command.ExecuteAsync(null!, settings);
            Assert.Equal(0, result);

            // Verify the transformation
            var modifiedCode = await File.ReadAllTextAsync(csFile);
            Assert.Contains("public void ProcessData(ProcessDataRequest args)", modifiedCode);
            Assert.Contains("if (args.isActive)", modifiedCode);
            Assert.Contains("Console.WriteLine(args.message);", modifiedCode);
            Assert.Contains("foreach (var num in args.numbers)", modifiedCode);
            Assert.Contains("public List<int> numbers { get; set; }", modifiedCode);
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }
}