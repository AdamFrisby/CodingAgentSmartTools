using Xunit;
using Cast.Tool.Core;
using System.IO;
using System.Threading.Tasks;

namespace Cast.Tool.Tests;

public class LspTests : IDisposable
{
    private readonly LspHelper _helper;
    private readonly string _testFilePath;

    public LspTests()
    {
        _helper = new LspHelper();
        
        // Create a temporary C# test file
        _testFilePath = Path.GetTempFileName();
        File.WriteAllText(_testFilePath, @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public string TestProperty { get; set; }
        
        public void TestMethod()
        {
            Console.WriteLine(""Hello World"");
        }
    }
}");
        
        // Rename to .cs extension
        var csFilePath = Path.ChangeExtension(_testFilePath, ".cs");
        File.Move(_testFilePath, csFilePath);
        _testFilePath = csFilePath;
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Fact]
    public void LspHelper_ValidateInputs_ValidFile_ShouldNotThrow()
    {
        // Arrange
        var settings = new LspSettings { FilePath = _testFilePath };

        // Act & Assert - Should not throw
        _helper.ValidateInputs(settings);
    }

    [Fact]
    public void LspHelper_ValidateInputs_InvalidFile_ShouldThrow()
    {
        // Arrange
        var settings = new LspSettings { FilePath = "nonexistent.cs" };

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => _helper.ValidateInputs(settings));
    }

    [Fact]
    public void LspHelper_ValidateInputs_NegativeLineNumber_ShouldThrow()
    {
        // Arrange
        var settings = new LspSettings { FilePath = _testFilePath, LineNumber = -1 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _helper.ValidateInputs(settings));
    }

    [Fact]
    public void LspHelper_ValidateInputs_NegativeColumnNumber_ShouldThrow()
    {
        // Arrange
        var settings = new LspSettings { FilePath = _testFilePath, ColumnNumber = -1 };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _helper.ValidateInputs(settings));
    }

    [Fact]
    public void LspHelper_CreatePosition_ShouldReturnCorrectPosition()
    {
        // Arrange
        var settings = new LspSettings { LineNumber = 5, ColumnNumber = 10 };

        // Act
        var position = _helper.CreatePosition(settings);

        // Assert
        Assert.Equal(5, position.Line);
        Assert.Equal(10, position.Character);
    }

    [Fact]
    public void LspHelper_CreateDocumentUri_ShouldReturnValidUri()
    {
        // Act
        var uri = _helper.CreateDocumentUri(_testFilePath);

        // Assert
        Assert.StartsWith("file://", uri);
        Assert.Contains(Path.GetFileName(_testFilePath), uri);
    }

    [Fact]
    public void LspHelper_GetLanguageId_CSharpFile_ShouldReturnCSharp()
    {
        // Act
        var languageId = _helper.GetLanguageId(_testFilePath);

        // Assert
        Assert.Equal("csharp", languageId);
    }

    [Fact]
    public void LspHelper_GetLanguageId_TypeScriptFile_ShouldReturnTypeScript()
    {
        // Act
        var languageId = _helper.GetLanguageId("test.ts");

        // Assert
        Assert.Equal("typescript", languageId);
    }

    [Fact]
    public void LspHelper_GetLanguageId_JavaScriptFile_ShouldReturnJavaScript()
    {
        // Act
        var languageId = _helper.GetLanguageId("test.js");

        // Assert
        Assert.Equal("javascript", languageId);
    }

    [Fact]
    public void LspHelper_GetLanguageId_PythonFile_ShouldReturnPython()
    {
        // Act
        var languageId = _helper.GetLanguageId("test.py");

        // Assert
        Assert.Equal("python", languageId);
    }

    [Fact]
    public void LspHelper_GetLanguageId_UnknownFile_ShouldReturnPlainText()
    {
        // Act
        var languageId = _helper.GetLanguageId("test.unknown");

        // Assert
        Assert.Equal("plaintext", languageId);
    }

    [Fact]
    public void LspHelper_GetDefaultServerPath_CSharpFile_ShouldReturnCSharpServerOrNull()
    {
        // Act
        var serverPath = _helper.GetDefaultServerPath(_testFilePath);

        // Assert
        // Should return null if no server is installed, or a path if found
        // This test just verifies the method doesn't throw
        Assert.True(serverPath == null || serverPath.Length > 0);
    }

    [Fact]
    public void LspPosition_Constructor_ShouldSetCorrectValues()
    {
        // Act
        var position = new LspPosition(10, 5);

        // Assert
        Assert.Equal(10, position.Line);
        Assert.Equal(5, position.Character);
    }

    [Fact]
    public void LspRange_Constructor_ShouldSetCorrectValues()
    {
        // Arrange
        var start = new LspPosition(1, 0);
        var end = new LspPosition(2, 10);

        // Act
        var range = new LspRange(start, end);

        // Assert
        Assert.Equal(start, range.Start);
        Assert.Equal(end, range.End);
        Assert.Equal(1, range.Start.Line);
        Assert.Equal(0, range.Start.Character);
        Assert.Equal(2, range.End.Line);
        Assert.Equal(10, range.End.Character);
    }

    [Fact]
    public async Task LspHelper_CreateLspClientAsync_WithInvalidServer_ShouldReturnNull()
    {
        // Arrange
        var settings = new LspSettings 
        { 
            FilePath = _testFilePath,
            ServerPath = "nonexistent-server" 
        };

        // Act
        var client = await _helper.CreateLspClientAsync(settings);

        // Assert
        Assert.Null(client);
    }

    [Fact]
    public void LspSettings_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var settings = new LspSettings();

        // Assert
        Assert.Equal(0, settings.LineNumber);
        Assert.Equal(0, settings.ColumnNumber);
        Assert.Null(settings.ServerPath);
        Assert.Null(settings.ServerArgs);
        Assert.Null(settings.Query);
    }
}