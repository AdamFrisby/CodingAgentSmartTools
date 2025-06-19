using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using Spectre.Console.Cli;
using Cast.Tool.Core;

namespace Cast.Tool.Commands
{
    public class ExtractLocalFunctionCommandSettings : CommandSettings
    {
        [CommandArgument(0, "<filePath>")]
        [Description("Path to the C# file")]
        public string FilePath { get; set; } = string.Empty;

        [CommandOption("--function-name")]
        [Description("Name for the new local function")]
        public string FunctionName { get; set; } = string.Empty;

        [CommandOption("--start-line")]
        [Description("Start line number of code to extract (1-based)")]
        public int StartLine { get; set; }

        [CommandOption("--end-line")]
        [Description("End line number of code to extract (1-based, inclusive)")]
        public int EndLine { get; set; }

        [CommandOption("--output")]
        [Description("Output file path (default: modify in place)")]
        public string? OutputPath { get; set; }

        [CommandOption("--dry-run")]
        [Description("Preview changes without applying them")]
        public bool DryRun { get; set; }
    }

    public class ExtractLocalFunctionCommand : Command<ExtractLocalFunctionCommandSettings>
    {
        public override int Execute(CommandContext context, ExtractLocalFunctionCommandSettings settings)
        {
            try
            {
                if (!File.Exists(settings.FilePath))
                {
                    AnsiConsole.MarkupLine("[red]Error: File not found: {0}[/]", settings.FilePath);
                    return 1;
                }

                if (string.IsNullOrWhiteSpace(settings.FunctionName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Function name is required[/]");
                    return 1;
                }

                if (settings.StartLine <= 0 || settings.EndLine <= 0 || settings.StartLine > settings.EndLine)
                {
                    AnsiConsole.MarkupLine("[red]Error: Invalid line range. Start line must be <= end line and both must be > 0[/]");
                    return 1;
                }

                var sourceCode = File.ReadAllText(settings.FilePath);
                var tree = CSharpSyntaxTree.ParseText(sourceCode);
                var root = tree.GetCompilationUnitRoot();

                var modifiedRoot = ExtractLocalFunction(root, settings.StartLine, settings.EndLine, settings.FunctionName);
                var modifiedCode = modifiedRoot.ToFullString();

                if (settings.DryRun)
                {
                    var originalContent = File.ReadAllText(settings.FilePath);
                    DiffUtility.DisplayDiff(originalContent, modifiedCode, settings.FilePath);
                    return 0;
                }

                var outputPath = settings.OutputPath ?? settings.FilePath;
                File.WriteAllText(outputPath, modifiedCode);

                AnsiConsole.MarkupLine("[green]Successfully extracted local function '{0}' from lines {1}-{2} in {3}[/]", 
                    settings.FunctionName, settings.StartLine, settings.EndLine, outputPath);

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Error: {0}[/]", ex.Message);
                return 1;
            }
        }

        private static CompilationUnitSyntax ExtractLocalFunction(
            CompilationUnitSyntax root, 
            int startLine, 
            int endLine, 
            string functionName)
        {
            var sourceText = root.SyntaxTree.GetText();
            
            // Find statements within the specified line range
            var targetStatements = new List<StatementSyntax>();
            var containingMethod = FindContainingMethod(root, startLine);
            
            if (containingMethod == null)
            {
                throw new InvalidOperationException($"No method found containing line {startLine}");
            }

            // Extract statements within the line range
            if (containingMethod.Body != null)
            {
                foreach (var statement in containingMethod.Body.Statements)
                {
                    var statementStart = sourceText.Lines.GetLineFromPosition(statement.SpanStart).LineNumber + 1;
                    var statementEnd = sourceText.Lines.GetLineFromPosition(statement.Span.End).LineNumber + 1;
                    
                    if (statementStart >= startLine && statementEnd <= endLine)
                    {
                        targetStatements.Add(statement);
                    }
                }
            }

            if (!targetStatements.Any())
            {
                throw new InvalidOperationException($"No statements found in the specified line range {startLine}-{endLine}");
            }

            // Analyze variables used in the extracted code
            var variableAnalysis = AnalyzeVariables(targetStatements, containingMethod);
            
            // Create parameter list for local function
            var parameters = variableAnalysis.Parameters
                .Select(p => SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                    .WithType(SyntaxFactory.IdentifierName(p.Type)))
                .ToArray();

            // Create local function
            var localFunction = SyntaxFactory.LocalFunctionStatement(
                SyntaxFactory.IdentifierName(variableAnalysis.ReturnType),
                functionName)
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
                .WithBody(SyntaxFactory.Block(targetStatements));

            // Create function call to replace extracted statements
            var functionCall = CreateFunctionCall(functionName, variableAnalysis);

            // Replace extracted statements with function call and add local function
            var newStatements = new List<StatementSyntax>();
            bool extractedStatementsReplaced = false;

            foreach (var statement in containingMethod.Body!.Statements)
            {
                var statementStart = sourceText.Lines.GetLineFromPosition(statement.SpanStart).LineNumber + 1;
                var statementEnd = sourceText.Lines.GetLineFromPosition(statement.Span.End).LineNumber + 1;
                
                if (statementStart >= startLine && statementEnd <= endLine)
                {
                    if (!extractedStatementsReplaced)
                    {
                        newStatements.Add(localFunction);
                        newStatements.Add(functionCall);
                        extractedStatementsReplaced = true;
                    }
                    // Skip the extracted statement
                }
                else
                {
                    newStatements.Add(statement);
                }
            }

            var newBody = SyntaxFactory.Block(newStatements);
            var newMethod = containingMethod.WithBody(newBody);

            return root.ReplaceNode(containingMethod, newMethod);
        }

        private static MethodDeclarationSyntax? FindContainingMethod(CompilationUnitSyntax root, int lineNumber)
        {
            var sourceText = root.SyntaxTree.GetText();
            var position = sourceText.Lines[lineNumber - 1].Start;

            return root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position, 1))
                .FirstAncestorOrSelf<MethodDeclarationSyntax>();
        }

        private static VariableAnalysis AnalyzeVariables(
            List<StatementSyntax> statements, 
            MethodDeclarationSyntax containingMethod)
        {
            var parameters = new List<ParameterInfo>();
            var returnType = "void";
            
            // Simple analysis - look for variable references
            // In a real implementation, this would use semantic analysis
            var usedVariables = new HashSet<string>();
            
            foreach (var statement in statements)
            {
                var identifiers = statement.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(id => id.Identifier.ValueText)
                    .ToList();
                
                foreach (var identifier in identifiers)
                {
                    // Simple heuristic: if it's a lowercase identifier, assume it's a variable
                    if (char.IsLower(identifier[0]) && identifier != "void")
                    {
                        usedVariables.Add(identifier);
                    }
                }
            }

            // Check if any statements return a value
            var hasReturn = statements.Any(s => s is ReturnStatementSyntax);
            if (hasReturn)
            {
                // Try to infer return type from method
                if (containingMethod.ReturnType != null)
                {
                    returnType = containingMethod.ReturnType.ToString();
                }
                else
                {
                    returnType = "object"; // Default fallback
                }
            }

            // Convert used variables to parameters (simplified approach)
            foreach (var variable in usedVariables)
            {
                parameters.Add(new ParameterInfo(variable, "object")); // Default type
            }

            return new VariableAnalysis(parameters, returnType);
        }

        private static StatementSyntax CreateFunctionCall(string functionName, VariableAnalysis analysis)
        {
            var arguments = analysis.Parameters
                .Select(p => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(p.Name)))
                .ToArray();

            var invocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(functionName),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

            if (analysis.ReturnType == "void")
            {
                return SyntaxFactory.ExpressionStatement(invocation);
            }
            else
            {
                return SyntaxFactory.ReturnStatement(invocation);
            }
        }

        private record ParameterInfo(string Name, string Type);
        private record VariableAnalysis(List<ParameterInfo> Parameters, string ReturnType);
    }
}