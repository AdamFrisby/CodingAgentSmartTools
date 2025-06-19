using Cast.Tool.Core;
using Xunit;

namespace Cast.Tool.Tests;

public class DiffUtilityTests
{
    [Fact]
    public void GenerateUnifiedDiff_IdenticalContent_ReturnsNoChangesMessage()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3";
        var modifiedContent = "Line 1\nLine 2\nLine 3";
        var filePath = "test.cs";

        // Act
        var diff = DiffUtility.GenerateUnifiedDiff(originalContent, modifiedContent, filePath);

        // Assert
        Assert.Equal("No changes would be made to test.cs", diff);
    }

    [Fact]
    public void GenerateUnifiedDiff_SingleLineAddition_ReturnsProperDiff()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3";
        var modifiedContent = "Line 1\nLine 2\nNew Line\nLine 3";
        var filePath = "test.cs";

        // Act
        var diff = DiffUtility.GenerateUnifiedDiff(originalContent, modifiedContent, filePath);

        // Assert
        Assert.Contains("--- test.cs", diff);
        Assert.Contains("+++ test.cs", diff);
        Assert.Contains("+New Line", diff);
    }

    [Fact]
    public void GenerateUnifiedDiff_SingleLineRemoval_ReturnsProperDiff()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nRemove Me\nLine 3";
        var modifiedContent = "Line 1\nLine 2\nLine 3";
        var filePath = "test.cs";

        // Act
        var diff = DiffUtility.GenerateUnifiedDiff(originalContent, modifiedContent, filePath);

        // Assert
        Assert.Contains("--- test.cs", diff);
        Assert.Contains("+++ test.cs", diff);
        Assert.Contains("-Remove Me", diff);
    }

    [Fact]
    public void GenerateUnifiedDiff_LineModification_ReturnsProperDiff()
    {
        // Arrange
        var originalContent = "Line 1\nOld Line\nLine 3";
        var modifiedContent = "Line 1\nNew Line\nLine 3";
        var filePath = "test.cs";

        // Act
        var diff = DiffUtility.GenerateUnifiedDiff(originalContent, modifiedContent, filePath);

        // Assert
        Assert.Contains("--- test.cs", diff);
        Assert.Contains("+++ test.cs", diff);
        Assert.Contains("-Old Line", diff);
        Assert.Contains("+New Line", diff);
    }

    [Fact]
    public void GenerateUnifiedDiff_UsingStatementAddition_ReturnsProperDiff()
    {
        // Arrange
        var originalContent = "using System;\n\nnamespace Test\n{\n    class Program\n    {\n    }\n}";
        var modifiedContent = "using System;\nusing System.Collections.Generic;\n\nnamespace Test\n{\n    class Program\n    {\n    }\n}";
        var filePath = "Program.cs";

        // Act
        var diff = DiffUtility.GenerateUnifiedDiff(originalContent, modifiedContent, filePath);

        // Assert
        Assert.Contains("--- Program.cs", diff);
        Assert.Contains("+++ Program.cs", diff);
        Assert.Contains("+using System.Collections.Generic;", diff);
        Assert.Contains("using System;", diff);
    }
}