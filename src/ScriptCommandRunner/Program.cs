using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ConsoleAppFramework;
using ScriptCommandRunner.Options;

try
{
    // Skip loading the file for --init so a corrupted appsettings.json
    // can still be regenerated with --init --force.
    var options = args is ["--init", ..]
        ? new ScriptCommandRunnerOptions()
        : LoadOptions();
    var commands = new ScriptCommands(options);

    var app = ConsoleApp.Create();
    app.Add("--init", commands.Init);
    app.Add("", commands.Run);
    await app.RunAsync(NormalizeArguments(args));
}
catch (Exception exception) when (exception is JsonException or IOException)
{
    Console.Error.WriteLine($"Failed to load {AppSettings.FileName}: {exception.Message}");
    Environment.ExitCode = (int)ExitCode.Error;
}

static ScriptCommandRunnerOptions LoadOptions()
{
    var appSettingsPath = Path.Combine(AppContext.BaseDirectory, AppSettings.FileName);

    if (!File.Exists(appSettingsPath))
    {
        return new ScriptCommandRunnerOptions();
    }

    using var stream = File.OpenRead(appSettingsPath);
    var context = AppSettingsJsonSerializerContext.Default.AppSettings;
    var appSettings = JsonSerializer.Deserialize(stream, context) ?? new AppSettings();
    var options = appSettings.ScriptCommandRunnerOptions ?? new ScriptCommandRunnerOptions();

    // Explicit JSON nulls can overwrite the property defaults.
    if (options.ScriptDirectory is null or [])
    {
        options.ScriptDirectory = [ScriptCommandRunnerOptions.DefaultScriptDirectory];
    }

    if (options.ExecutableArguments is null)
    {
        options.ExecutableArguments = [];
    }

    return options;
}

static string[] NormalizeArguments(string[] arguments)
{
    return arguments switch
    {
        ["--init", ..] => arguments,
        [_, "--", ..] => arguments,
        [var command, var subCommand, .. var remaining] => [command, "--", subCommand, .. remaining],
        _ => arguments,
    };
}

internal sealed class ScriptCommands(ScriptCommandRunnerOptions options)
{
    public async Task<int> Init(bool force = false, CancellationToken cancellationToken = default)
    {
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, AppSettings.FileName);

        if (!force && File.Exists(appSettingsPath))
        {
            Console.Error.WriteLine($"File already exists: {appSettingsPath}");
            Console.Error.WriteLine("Use --force to overwrite it.");
            return (int)ExitCode.Error;
        }

        var temporaryPath = $"{appSettingsPath}.tmp";

        try
        {
            var fileStreamOptions = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            await using (var stream = new FileStream(temporaryPath, fileStreamOptions))
            {
                var context = AppSettingsJsonSerializerContext.Default.AppSettings;
                await JsonSerializer.SerializeAsync(stream, new AppSettings(), context, cancellationToken);
            }

            File.Move(temporaryPath, appSettingsPath, overwrite: force);
        }
        catch (IOException) when (!force && File.Exists(appSettingsPath))
        {
            TryDelete(temporaryPath);
            Console.Error.WriteLine($"File already exists: {appSettingsPath}");
            Console.Error.WriteLine("Use --force to overwrite it.");
            return (int)ExitCode.Error;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            Console.Error.WriteLine($"Failed to create {appSettingsPath}: {exception.Message}");
            return (int)ExitCode.Error;
        }

        Console.WriteLine($"Created: {appSettingsPath}");
        return (int)ExitCode.Success;

        static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Leave the temporary file behind if it cannot be deleted.
            }
        }
    }

    public Task<int> Run([Argument] string command, ConsoleAppContext context, CancellationToken cancellationToken)
    {
        if (options.Validate() is { } configurationError)
        {
            Console.Error.WriteLine(configurationError);
            return Task.FromResult((int)ExitCode.Error);
        }

        if (!IsValidCommandName(command))
        {
            Console.Error.WriteLine($"Invalid command name: {command}");
            return Task.FromResult((int)ExitCode.Error);
        }

        static bool IsValidCommandName(string command) => command switch
        {
            null or "" or "." or ".." => false,
            _ => !command.Contains('/') && !command.Contains('\\')
        };

        var applicationDirectory = AppContext.BaseDirectory;
        var scriptName = $"{command}{options.ScriptExtension}";
        var scriptPaths = GetScriptPaths(applicationDirectory, options.ScriptDirectory, scriptName);

        if (Array.Find(scriptPaths, File.Exists) is not { } scriptPath)
        {
            Console.Error.WriteLine($"Command not found: {command}");

            foreach (var candidatePath in scriptPaths)
            {
                Console.Error.WriteLine($"  {candidatePath}");
            }

            return Task.FromResult((int)ExitCode.Error);
        }

        var startInfo = CreateStartInfo(applicationDirectory, options, scriptPath, context.EscapedArguments);

        return RunProcessAsync(startInfo, scriptPath, cancellationToken);
    }

    private static ProcessStartInfo CreateStartInfo(
        string applicationDirectory,
        ScriptCommandRunnerOptions options,
        string scriptPath,
        ReadOnlySpan<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Executable,
            UseShellExecute = false,
            WorkingDirectory = applicationDirectory,
        };

        foreach (var argument in options.ExecutableArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<int> RunProcessAsync(
        ProcessStartInfo startInfo,
        string scriptPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The process could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Failed to start {scriptPath}: {exception.Message}");
            return (int)ExitCode.Error;
        }

        static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // The process has already exited or cannot be terminated.
            }
        }
    }

    private static string[] GetScriptPaths(
        string applicationDirectory,
        string[] scriptDirectories,
        string scriptName)
    {
        return Array.ConvertAll(
            scriptDirectories,
            scriptDirectory => GetScriptPath(applicationDirectory, scriptDirectory, scriptName));
    }

    private static string GetScriptPath(
        string applicationDirectory,
        string scriptDirectory,
        string scriptName)
    {
        var configuredDirectory = Path.IsPathRooted(scriptDirectory)
            ? scriptDirectory
            : Path.Combine(applicationDirectory, scriptDirectory);
        var resolvedDirectory = Path.GetFullPath(configuredDirectory);

        return Path.Combine(resolvedDirectory, scriptName);
    }
}
