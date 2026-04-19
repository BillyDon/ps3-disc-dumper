using System.IO.Compression;
using Ps3DiscDumper;
using Ps3DiscDumper.Utils;
using Spectre.Console;

// Parse arguments
var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
var input = "";
var output = Path.Combine(Directory.GetCurrentDirectory(), "output");
var irdCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ps3-iso-dumper", "ird");
var workers = 1;

for (int i = 0; i < cmdArgs.Count; i++)
{
    if (cmdArgs[i] == "--output" || cmdArgs[i] == "-o")
        output = cmdArgs[++i];
    else if (cmdArgs[i] == "--ird-cache")
        irdCache = cmdArgs[++i];
    else if (cmdArgs[i] == "--workers" || cmdArgs[i] == "-w")
        workers = int.Parse(cmdArgs[++i]);
    else if (!cmdArgs[i].StartsWith("-"))
        input = cmdArgs[i];
}

if (string.IsNullOrEmpty(input))
{
    AnsiConsole.MarkupLine("[red]Error: missing input (ISO file, ZIP, or directory)[/]");
    AnsiConsole.MarkupLine("Usage: ps3dump <input> [--output DIR] [--ird-cache DIR] [--workers N]");
    Environment.Exit(1);
}

await HandleCommand(input, output, irdCache, workers);

async Task HandleCommand(string input, string output, string irdCache, int workers)
{
    try
    {
        // Resolve input: file, directory, or glob
        var isoFiles = ResolveInput(input);
        if (isoFiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]Error: No ISO or ZIP files found in {input}[/]");
            Environment.Exit(1);
        }

        AnsiConsole.MarkupLine($"[green]Found {isoFiles.Count} game(s) to process[/]");
        AnsiConsole.WriteLine();

        // Create output directory
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(irdCache);

        // Prepare list of (isoPath, tempDir, isZip)
        var jobs = new List<(string isoPath, string? tempDir, bool isZip)>();
        foreach (var file in isoFiles)
        {
            if (file.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                jobs.Add((file, null, false));
            else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tempDir = Path.Combine(Path.GetTempPath(), $"ps3dump_{Guid.NewGuid():N}");
                jobs.Add((file, tempDir, true));
            }
        }

        // Process sequentially (I/O bound on USB JBOD, benefit from parallel is minimal)
        int successCount = 0, failureCount = 0;
        var failedFiles = new List<string>();

        for (int i = 0; i < jobs.Count; i++)
        {
            var (inputFile, tempDir, isZip) = jobs[i];
            var displayName = Path.GetFileNameWithoutExtension(inputFile);
            AnsiConsole.MarkupLine($"\n[cyan][[{i + 1}/{jobs.Count}]][/] Processing: {displayName}");

            string isoPath = inputFile;
            try
            {
                // Extract ZIP if needed
                if (isZip)
                {
                    AnsiConsole.WriteLine("  Extracting ZIP...");
                    Directory.CreateDirectory(tempDir!);
                    using (var zip = ZipFile.OpenRead(inputFile))
                    {
                        var isoEntry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase));
                        if (isoEntry == null)
                        {
                            AnsiConsole.MarkupLine("  [red]✗ No ISO file found in ZIP[/]");
                            failureCount++;
                            failedFiles.Add($"{displayName} (no ISO in ZIP)");
                            continue;
                        }
                        isoPath = Path.Combine(tempDir, isoEntry.Name);
                        isoEntry.ExtractToFile(isoPath, overwrite: true);
                    }
                }

                // Dump ISO
                await DumpIso(isoPath, output, irdCache, displayName);
                successCount++;
                AnsiConsole.MarkupLine("  [green]✓ Completed[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"  [red]✗ Failed: {ex.Message}[/]");
                failureCount++;
                failedFiles.Add($"{displayName} ({ex.Message})");
            }
            finally
            {
                // Clean up temp directory
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch { }
                }
            }
        }

        // Summary
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Summary[/]: [green]{successCount} succeeded[/], [red]{failureCount} failed[/]");
        if (failedFiles.Count > 0)
        {
            AnsiConsole.MarkupLine("[red]Failed games:[/]");
            foreach (var f in failedFiles)
                AnsiConsole.MarkupLine($"  • {f}");
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Fatal error: {ex.Message}[/]");
        Environment.Exit(1);
    }
}

async Task DumpIso(string isoPath, string outputDir, string irdCache, string displayName)
{
    var dumper = new Dumper();
    try
    {
        // Detect ISO
        AnsiConsole.WriteLine("  Detecting...");
        dumper.DetectIso(isoPath);
        AnsiConsole.WriteLine($"  Title: {dumper.Title}");
        AnsiConsole.WriteLine($"  Product Code: {dumper.ProductCode}");

        // Find key
        AnsiConsole.WriteLine("  Finding decryption key...");
        await dumper.FindDiscKeyAsync(irdCache);
        AnsiConsole.WriteLine($"  Key Source: {dumper.DiscKeyType} ({Path.GetFileName(dumper.DiscKeyFilename)})");

        // Dump with progress
        AnsiConsole.WriteLine("  Dumping files...");
        var progressTask = AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn()
            );

        await progressTask.StartAsync(async ctx =>
        {
            var task = ctx.AddTask($"[green]{displayName}[/]", maxValue: 100);

            // Start dump in background
            var dumpTask = dumper.DumpAsync(outputDir);

            // Poll progress
            while (!dumpTask.IsCompleted)
            {
                await Task.Delay(500);
                if (dumper.TotalSectors > 0)
                {
                    var percent = (double)dumper.ProcessedSectors / dumper.TotalSectors * 100;
                    task.Value = percent;
                }
            }

            await dumpTask; // Wait for completion and propagate exceptions
            task.Value = 100;
        });

        if (dumper.BrokenFiles.Count > 0)
        {
            AnsiConsole.MarkupLine($"  [yellow]⚠ {dumper.BrokenFiles.Count} broken file(s):[/]");
            foreach (var (filename, error) in dumper.BrokenFiles)
                AnsiConsole.MarkupLine($"    • {filename}: {error}");
        }
    }
    finally
    {
        dumper.Dispose();
    }
}

List<string> ResolveInput(string input)
{
    var files = new List<string>();

    if (File.Exists(input))
    {
        // Single file (ISO or ZIP)
        if (input.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            files.Add(input);
    }
    else if (Directory.Exists(input))
    {
        // Directory: scan for .iso and .zip files
        files.AddRange(Directory.GetFiles(input, "*.iso", SearchOption.TopDirectoryOnly));
        files.AddRange(Directory.GetFiles(input, "*.zip", SearchOption.TopDirectoryOnly));
    }

    return files.OrderBy(f => Path.GetFileName(f)).ToList();
}
