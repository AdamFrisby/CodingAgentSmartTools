using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Cast.Tool.Core;

public class RefactoringEngine
{
    public async Task<(Document document, SyntaxTree tree, SemanticModel model)> LoadDocumentAsync(string filePath)
    {
        var sourceText = await File.ReadAllTextAsync(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
        
        // Create a minimal compilation for semantic analysis
        var compilation = CSharpCompilation.Create(
            assemblyName: "TempAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        // Create a workspace and document for refactoring operations
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TempProject",
            "TempProject",
            LanguageNames.CSharp,
            metadataReferences: GetMetadataReferences(),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var project = workspace.AddProject(projectInfo);
        var sourceTextObj = SourceText.From(sourceText);
        var document = project.AddDocument(Path.GetFileName(filePath), sourceTextObj);

        return (document, syntaxTree, semanticModel);
    }

    public TextSpan GetTextSpanFromPosition(SyntaxTree tree, int lineNumber, int columnNumber)
    {
        var text = tree.GetText();
        var line = text.Lines[lineNumber - 1]; // Convert to 0-based
        var position = line.Start + columnNumber;
        return new TextSpan(position, 1);
    }

    public async Task<string> ApplyChangesAsync(Document document, SyntaxNode newRoot)
    {
        var newDocument = document.WithSyntaxRoot(newRoot);
        var text = await newDocument.GetTextAsync();
        return text.ToString();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();
        
        // Add basic .NET references
        var dotnetAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (dotnetAssemblyPath != null)
        {
            references.Add(MetadataReference.CreateFromFile(Path.Combine(dotnetAssemblyPath, "System.Runtime.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(dotnetAssemblyPath, "System.Private.CoreLib.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(dotnetAssemblyPath, "System.Console.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(dotnetAssemblyPath, "System.Collections.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(dotnetAssemblyPath, "System.Linq.dll")));
        }

        return references;
    }
}