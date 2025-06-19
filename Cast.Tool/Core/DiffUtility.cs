using System.Text;
using Spectre.Console;

namespace Cast.Tool.Core;

/// <summary>
/// Utility class for generating unified diff output for dry-run preview
/// </summary>
public static class DiffUtility
{
    /// <summary>
    /// Generates a unified diff between original and modified content
    /// </summary>
    /// <param name="originalContent">Original file content</param>
    /// <param name="modifiedContent">Modified file content</param>
    /// <param name="filePath">File path for the diff header</param>
    /// <returns>Unified diff string</returns>
    public static string GenerateUnifiedDiff(string originalContent, string modifiedContent, string filePath)
    {
        if (originalContent == modifiedContent)
        {
            return $"No changes would be made to {filePath}";
        }

        var originalLines = originalContent.Split('\n');
        var modifiedLines = modifiedContent.Split('\n');
        
        var diff = new StringBuilder();
        diff.AppendLine($"--- {filePath}");
        diff.AppendLine($"+++ {filePath}");
        
        var diffResult = ComputeDiff(originalLines, modifiedLines);
        if (diffResult.Count > 0)
        {
            diff.AppendLine($"@@ -{diffResult[0].OriginalStart},{diffResult[0].OriginalCount} +{diffResult[0].ModifiedStart},{diffResult[0].ModifiedCount} @@");
            
            foreach (var hunk in diffResult)
            {
                foreach (var line in hunk.Lines)
                {
                    diff.AppendLine(line);
                }
            }
        }
        
        return diff.ToString();
    }

    /// <summary>
    /// Displays the diff with colored output using Spectre.Console
    /// </summary>
    /// <param name="originalContent">Original file content</param>
    /// <param name="modifiedContent">Modified file content</param>
    /// <param name="filePath">File path for the diff header</param>
    public static void DisplayDiff(string originalContent, string modifiedContent, string filePath)
    {
        if (originalContent == modifiedContent)
        {
            AnsiConsole.MarkupLine($"[yellow]No changes would be made to {filePath}[/]");
            return;
        }

        var originalLines = originalContent.Split('\n');
        var modifiedLines = modifiedContent.Split('\n');
        
        AnsiConsole.MarkupLine($"[blue]--- {filePath}[/]");
        AnsiConsole.MarkupLine($"[blue]+++ {filePath}[/]");
        
        var diffResult = ComputeDiff(originalLines, modifiedLines);
        if (diffResult.Count > 0)
        {
            foreach (var hunk in diffResult)
            {
                AnsiConsole.MarkupLine($"[cyan]@@ -{hunk.OriginalStart},{hunk.OriginalCount} +{hunk.ModifiedStart},{hunk.ModifiedCount} @@[/]");
                
                foreach (var line in hunk.Lines)
                {
                    if (line.StartsWith("-"))
                    {
                        AnsiConsole.MarkupLine($"[red]{line.EscapeMarkup()}[/]");
                    }
                    else if (line.StartsWith("+"))
                    {
                        AnsiConsole.MarkupLine($"[green]{line.EscapeMarkup()}[/]");
                    }
                    else
                    {
                        AnsiConsole.WriteLine(line);
                    }
                }
            }
        }
    }

    private static List<DiffHunk> ComputeDiff(string[] originalLines, string[] modifiedLines)
    {
        var hunks = new List<DiffHunk>();
        var lcs = ComputeLCS(originalLines, modifiedLines);
        
        var originalIndex = 0;
        var modifiedIndex = 0;
        var contextLines = 3; // Standard diff context
        
        while (originalIndex < originalLines.Length || modifiedIndex < modifiedLines.Length)
        {
            var hunk = new DiffHunk();
            var hunkLines = new List<string>();
            
            // Find next difference
            while (originalIndex < originalLines.Length && modifiedIndex < modifiedLines.Length &&
                   originalLines[originalIndex] == modifiedLines[modifiedIndex])
            {
                originalIndex++;
                modifiedIndex++;
            }
            
            if (originalIndex >= originalLines.Length && modifiedIndex >= modifiedLines.Length)
            {
                break;
            }
            
            // Add context before
            var contextStart = Math.Max(0, originalIndex - contextLines);
            for (int i = contextStart; i < originalIndex; i++)
            {
                hunkLines.Add(" " + originalLines[i]);
            }
            
            hunk.OriginalStart = contextStart + 1;
            hunk.ModifiedStart = Math.Max(0, modifiedIndex - (originalIndex - contextStart)) + 1;
            
            // Add removed lines
            var removedCount = 0;
            while (originalIndex < originalLines.Length && 
                   (modifiedIndex >= modifiedLines.Length || originalLines[originalIndex] != modifiedLines[modifiedIndex]))
            {
                // Check if this line exists later in modified
                var foundLater = false;
                for (int j = modifiedIndex; j < Math.Min(modifiedIndex + 10, modifiedLines.Length); j++)
                {
                    if (originalLines[originalIndex] == modifiedLines[j])
                    {
                        foundLater = true;
                        break;
                    }
                }
                
                if (!foundLater)
                {
                    hunkLines.Add("-" + originalLines[originalIndex]);
                    removedCount++;
                    originalIndex++;
                }
                else
                {
                    break;
                }
            }
            
            // Add added lines
            var addedCount = 0;
            while (modifiedIndex < modifiedLines.Length && 
                   (originalIndex >= originalLines.Length || modifiedLines[modifiedIndex] != originalLines[originalIndex]))
            {
                // Check if this line exists later in original
                var foundLater = false;
                for (int j = originalIndex; j < Math.Min(originalIndex + 10, originalLines.Length); j++)
                {
                    if (modifiedLines[modifiedIndex] == originalLines[j])
                    {
                        foundLater = true;
                        break;
                    }
                }
                
                if (!foundLater)
                {
                    hunkLines.Add("+" + modifiedLines[modifiedIndex]);
                    addedCount++;
                    modifiedIndex++;
                }
                else
                {
                    break;
                }
            }
            
            // Add context after
            var contextEnd = Math.Min(originalLines.Length, originalIndex + contextLines);
            for (int i = originalIndex; i < contextEnd; i++)
            {
                hunkLines.Add(" " + originalLines[i]);
            }
            
            hunk.OriginalCount = removedCount + (contextEnd - Math.Max(0, originalIndex - contextLines));
            hunk.ModifiedCount = addedCount + (contextEnd - Math.Max(0, originalIndex - contextLines));
            hunk.Lines = hunkLines;
            
            if (hunkLines.Any(l => l.StartsWith("-") || l.StartsWith("+")))
            {
                hunks.Add(hunk);
            }
            
            // Move past matched section
            while (originalIndex < originalLines.Length && modifiedIndex < modifiedLines.Length &&
                   originalLines[originalIndex] == modifiedLines[modifiedIndex])
            {
                originalIndex++;
                modifiedIndex++;
            }
        }
        
        return hunks;
    }
    
    private static int[,] ComputeLCS(string[] originalLines, string[] modifiedLines)
    {
        var m = originalLines.Length;
        var n = modifiedLines.Length;
        var lcs = new int[m + 1, n + 1];
        
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (originalLines[i - 1] == modifiedLines[j - 1])
                {
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                }
                else
                {
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }
        }
        
        return lcs;
    }
    
    private class DiffHunk
    {
        public int OriginalStart { get; set; }
        public int OriginalCount { get; set; }
        public int ModifiedStart { get; set; }
        public int ModifiedCount { get; set; }
        public List<string> Lines { get; set; } = new();
    }
}