
using PresetsCopyTool;
using Spectre.Console;
using System.Globalization;

// Parse command-line arguments
var arguments = ParseArguments(args);

string[] requiredArguments =
[
    "input_folder",
    "output_folder",
    "prefix_pattern"
];

var missingArguments = requiredArguments
    .Where(key => !arguments.ContainsKey(key) ||
                  string.IsNullOrWhiteSpace(arguments[key]))
    .ToArray();

if (missingArguments.Length > 0)
{
    AnsiConsole.MarkupLine("[red]Missing required arguments:[/]");
    AnsiConsole.MarkupLine(
        $"[yellow]{string.Join(", ", missingArguments)}[/]");

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Usage:[/]");
    AnsiConsole.MarkupLine(
        "PresetsCopyTool.exe --input_folder=\"C:\\Source\" " +
        "--output_folder=\"C:\\Output\" " +
        "--prefix_pattern=\"yyyyMMdd_\"");

    return;
}

string sourceFolder = arguments["input_folder"];
string outputFolder = arguments["output_folder"];
string prefixPattern = arguments["prefix_pattern"];

string prefix;

try
{
    prefix = DateTime.Now.ToString(
        prefixPattern,
        CultureInfo.InvariantCulture);
}
catch (FormatException)
{
    AnsiConsole.MarkupLine(
        $"[red]Invalid prefix pattern: {Markup.Escape(prefixPattern)}[/]");
    return;
}

// Header
AnsiConsole.Clear();

AnsiConsole.Write(
    new FigletText("Presets Copy Tool")
        .LeftJustified()
        .Color(Color.Cyan1));

AnsiConsole.WriteLine();

// Scan files
if (!Directory.Exists(sourceFolder))
{
    AnsiConsole.MarkupLine("[red]Source folder does not exist![/]");
    return;
}

FileInfo[] files;

try
{
    files = new DirectoryInfo(sourceFolder).GetFiles();
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine(
        $"[red]Failed to scan source folder:[/] {Markup.Escape(ex.Message)}");
    return;
}

long totalSize = files.Sum(f => f.Length);

AnsiConsole.Write(new Rule("[cyan]Transfer configuration[/]"));

var table = new Table()
    .Border(TableBorder.Rounded)
    .AddColumn("Property")
    .AddColumn("Value");

table.AddRow("Source folder", Markup.Escape(sourceFolder));
table.AddRow("Output folder", Markup.Escape(outputFolder));
table.AddRow("Files to copy", files.Length.ToString());
table.AddRow("Total size", FormatSize(totalSize));
table.AddRow("Prefix", $"[green]{Markup.Escape(prefix)}[/]");

AnsiConsole.Write(table);

// File list
if (files.Length > 0)
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[cyan]Files:[/]");

    var fileTable = new Table()
        .Border(TableBorder.Simple)
        .AddColumn("Original name")
        .AddColumn("New name")
        .AddColumn(new TableColumn("Size").RightAligned());

    foreach (var file in files.OrderBy(f => f.Name))
    {
        fileTable.AddRow(
            Markup.Escape(file.Name),
            Markup.Escape(prefix + file.Name),
            FormatSize(file.Length));
    }

    AnsiConsole.Write(fileTable);
}

if (files.Length == 0)
{
    AnsiConsole.MarkupLine("\n[yellow]No files found.[/]");
    return;
}

// Confirmation
AnsiConsole.WriteLine();

var action = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("[cyan]Select action[/]")
        .AddChoices("Execute", "Exit"));

if (action != "Execute")
    return;

// Create output directory
try
{
    Directory.CreateDirectory(outputFolder);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine(
        $"[red]Cannot create output folder:[/] {Markup.Escape(ex.Message)}");
    return;
}

// Cancellation support
using var cancellationSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationSource.Cancel();
};

var service = new FileTransferService();

var stopwatch = System.Diagnostics.Stopwatch.StartNew();

FileTransferResult result = new(0, 0, 0, 0, []);

try
{
    AnsiConsole.WriteLine();

    await AnsiConsole.Progress()
        .AutoClear(false)
        .HideCompleted(false)
        .Columns(
            new SpinnerColumn(),
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new RemainingTimeColumn())
        .StartAsync(async ctx =>
        {
            var progressTask = ctx.AddTask(
                "[cyan]Copying files[/]",
                maxValue: Math.Max(1, totalSize));

            result = await service.CopyAsync(
                files,
                outputFolder,
                prefix,
                bytes => progressTask.Increment(bytes),
                cancellationSource.Token);

            progressTask.Value = progressTask.MaxValue;
        });
}
catch (OperationCanceledException)
{
    stopwatch.Stop();

    AnsiConsole.MarkupLine(
        "\n[yellow]Operation cancelled by user.[/]");

    AnsiConsole.MarkupLine(
        $"Copied files: {result.CopiedFiles}");

    AnsiConsole.MarkupLine(
        $"Copied size: {FormatSize(result.CopiedBytes)}");

    return;
}

stopwatch.Stop();

AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule("[green]Transfer completed[/]"));

var resultTable = new Table()
    .Border(TableBorder.Rounded)
    .AddColumn("Property")
    .AddColumn("Value");

resultTable.AddRow("Copied files", $"[green]{result.CopiedFiles}[/]");
resultTable.AddRow("Skipped files", $"[yellow]{result.SkippedFiles}[/]");
resultTable.AddRow("Failed files", $"[red]{result.FailedFiles}[/]");
resultTable.AddRow("Copied size", FormatSize(result.CopiedBytes));
resultTable.AddRow(
    "Duration",
    stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff"));

AnsiConsole.Write(resultTable);

// Errors
if (result.Errors.Count > 0)
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[red]Errors:[/]");

    foreach (var error in result.Errors)
    {
        AnsiConsole.MarkupLine(
            $"[red]- {Markup.Escape(error)}[/]");
    }
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < args.Length; i++)
    {
        string argument = args[i];

        if (!argument.StartsWith("--"))
            continue;

        string option = argument[2..];

        int separator = option.IndexOf('=');

        if (separator >= 0)
        {
            string key = option[..separator];
            string value = option[(separator + 1)..];

            result[key] = value.Trim('"');
        }
        else if (i + 1 < args.Length &&
                 !args[i + 1].StartsWith("--"))
        {
            result[option] = args[++i].Trim('"');
        }
    }

    return result;
}

static string FormatSize(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];

    double size = bytes;
    int unit = 0;

    while (size >= 1024 && unit < units.Length - 1)
    {
        size /= 1024;
        unit++;
    }

    return $"{size:N2} {units[unit]}";
}
