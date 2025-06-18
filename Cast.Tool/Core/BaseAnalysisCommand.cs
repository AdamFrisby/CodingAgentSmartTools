using System.ComponentModel;
using Spectre.Console.Cli;

namespace Cast.Tool.Core;

public abstract class BaseAnalysisCommand : Command<BaseAnalysisCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The C# source file to analyze")]
        public string FilePath { get; init; } = string.Empty;

        [CommandOption("-l|--line")]
        [Description("Line number (1-based) for symbol location")]
        [DefaultValue(1)]
        public int LineNumber { get; init; } = 1;

        [CommandOption("-c|--column")]
        [Description("Column number (0-based) for symbol location")]
        [DefaultValue(0)]
        public int ColumnNumber { get; init; } = 0;

        [CommandOption("-p|--pattern")]
        [Description("Search pattern for symbols")]
        public string? Pattern { get; init; }

        [CommandOption("-t|--type")]
        [Description("Type name to analyze")]
        public string? TypeName { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return ExecuteAsync(context, settings).GetAwaiter().GetResult();
    }

    public abstract Task<int> ExecuteAsync(CommandContext context, Settings settings);

    protected void ValidateInputs(Settings settings)
    {
        if (!File.Exists(settings.FilePath))
        {
            throw new FileNotFoundException($"File not found: {settings.FilePath}");
        }

        if (!settings.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only C# files (.cs) are supported");
        }

        if (settings.LineNumber < 1)
        {
            throw new ArgumentException("Line number must be 1 or greater");
        }

        if (settings.ColumnNumber < 0)
        {
            throw new ArgumentException("Column number must be 0 or greater");
        }
    }

    protected void OutputResult(string filePath, int lineNumber, string lineContent)
    {
        Console.WriteLine($"{filePath}:{lineNumber} {lineContent.Trim()}");
    }
}