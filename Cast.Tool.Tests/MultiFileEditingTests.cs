using System.IO;
using System.Threading.Tasks;
using Xunit;
using Cast.Tool.Core;
using Cast.Tool.Commands;

namespace Cast.Tool.Tests;

public class MultiFileEditingTests
{
    [Fact]
    public async Task ExtractBaseClass_DryRun_ShouldShowMultipleFiles()
    {
        // Arrange
        var testCode = @"using System;

namespace MyProject
{
    public class Employee
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public decimal Salary { get; set; }
        
        public void DisplayInfo()
        {
            Console.WriteLine($""Name: {Name}, Age: {Age}"");
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
            var command = new ExtractBaseClassCommand();
            var settings = new ExtractBaseClassCommandSettings
            {
                FilePath = csFile,
                ClassName = "Employee",
                BaseClassName = "Person",
                Members = "Name,Age,DisplayInfo",
                DryRun = true
            };

            var result = command.Execute(null!, settings);

            // Assert
            Assert.Equal(0, result);
            // The actual output verification would require capturing console output
            // For now, we verify the command executes successfully in dry-run mode
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public async Task ExtractInterface_DryRun_ShouldShowMultipleFiles()
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
        
        public int Subtract(int a, int b)
        {
            return a - b;
        }
        
        private void LogOperation(string operation)
        {
            Console.WriteLine($""Operation: {operation}"");
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
            var command = new ExtractInterfaceCommand();
            var settings = new ExtractInterfaceCommandSettings
            {
                FilePath = csFile,
                ClassName = "Calculator",
                InterfaceName = "ICalculator",
                DryRun = true
            };

            var result = command.Execute(null!, settings);

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public async Task MoveTypeToMatchingFile_DryRun_ShouldShowFileCreation()
    {
        // Arrange
        var testCode = @"using System;

namespace MyProject
{
    public class MainClass
    {
        public void DoSomething() { }
    }
    
    public class HelperClass
    {
        public void Help() { }
    }
}";
        
        var tempFile = Path.GetTempFileName();
        var csFile = Path.ChangeExtension(tempFile, ".cs");
        File.Move(tempFile, csFile);
        await File.WriteAllTextAsync(csFile, testCode);

        try
        {
            // Act
            var command = new MoveTypeToMatchingFileCommand();
            var settings = new MoveTypeToMatchingFileCommandSettings
            {
                FilePath = csFile,
                TypeName = "HelperClass",
                DryRun = true
            };

            var result = command.Execute(null!, settings);

            // Assert
            Assert.Equal(0, result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(csFile))
                File.Delete(csFile);
        }
    }

    [Fact]
    public void DiffUtility_DisplayMultiFileDiff_ShouldHandleMultipleFiles()
    {
        // Arrange
        var fileChanges = new Dictionary<string, (string original, string modified)>
        {
            ["Test1.cs"] = ("using System;", "using System;\nusing System.Collections.Generic;"),
            ["Test2.cs"] = ("", "public class NewClass { }"),
            ["Test3.cs"] = ("public class OldClass { }", "public class RenamedClass { }")
        };

        // Act & Assert - The method should execute without throwing
        DiffUtility.DisplayMultiFileDiff(fileChanges);
        DiffUtility.DisplayFileSummary(fileChanges);
        
        // Test with empty changes
        DiffUtility.DisplayMultiFileDiff(new Dictionary<string, (string, string)>());
        DiffUtility.DisplayFileSummary(new Dictionary<string, (string, string)>());
    }

    [Fact]
    public void RefactoringEngine_ResolveProjectPath_ShouldFindProjectFile()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var projectDirName = Guid.NewGuid().ToString();
        var projectDir = Path.Combine(tempDir, projectDirName);
        Directory.CreateDirectory(projectDir);
        
        var csprojFile = Path.Combine(projectDir, "TestProject.csproj");
        var sourceFile = Path.Combine(projectDir, "Source.cs");
        
        try
        {
            File.WriteAllText(csprojFile, "<Project></Project>");
            File.WriteAllText(sourceFile, "class Test {}");

            // Act
            var resolvedPath = RefactoringEngine.ResolveProjectPath(sourceFile);

            // Assert
            Assert.Equal(projectDir, resolvedPath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(projectDir))
                Directory.Delete(projectDir, true);
        }
    }

    [Fact]
    public void RefactoringEngine_GetRelativePathFromProject_ShouldReturnCorrectPath()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var projectDirName = Guid.NewGuid().ToString();
        var projectDir = Path.Combine(tempDir, projectDirName);
        var sourceDir = Path.Combine(projectDir, "Models");
        Directory.CreateDirectory(sourceDir);
        
        var csprojFile = Path.Combine(projectDir, "TestProject.csproj");
        var sourceFile = Path.Combine(sourceDir, "User.cs");
        
        try
        {
            File.WriteAllText(csprojFile, "<Project></Project>");
            File.WriteAllText(sourceFile, "class User {}");

            // Act
            var relativePath = RefactoringEngine.GetRelativePathFromProject(sourceFile, projectDir);

            // Assert
            Assert.Equal("Models" + Path.DirectorySeparatorChar + "User.cs", relativePath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(projectDir))
                Directory.Delete(projectDir, true);
        }
    }

    [Fact]
    public async Task SyncNamespaceWithFolder_WithProjectPath_ShouldCalculateCorrectNamespace()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var projectDirName = Guid.NewGuid().ToString();
        var projectDir = Path.Combine(tempDir, projectDirName);
        var modelsDir = Path.Combine(projectDir, "Models");
        Directory.CreateDirectory(modelsDir);
        
        var csprojFile = Path.Combine(projectDir, $"{projectDirName}.csproj");
        var sourceFile = Path.Combine(modelsDir, "User.cs");
        var testCode = @"using System;

namespace WrongNamespace
{
    public class User
    {
        public string Name { get; set; }
    }
}";
        
        try
        {
            File.WriteAllText(csprojFile, "<Project></Project>");
            await File.WriteAllTextAsync(sourceFile, testCode);

            // Act
            var command = new SyncNamespaceWithFolderCommand();
            var settings = new SyncNamespaceWithFolderCommand.Settings
            {
                FilePath = sourceFile,
                ProjectPath = projectDir,
                DryRun = true
            };

            var result = await command.ExecuteAsync(null!, settings);

            // Assert
            Assert.Equal(0, result);
            
            // Verify the file wasn't actually modified (dry run)
            var content = await File.ReadAllTextAsync(sourceFile);
            Assert.Contains("WrongNamespace", content);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(projectDir))
                Directory.Delete(projectDir, true);
        }
    }
}