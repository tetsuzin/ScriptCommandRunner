using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ConsoleAppFramework;
using ScriptCommandRunner.Options;

var app = ConsoleApp.Create()
    .ConfigureDefaultConfiguration()
    .ConfigureServices();

app.Add<ScriptCommands>();
await app.RunAsync(NormalizeArguments(args));

static string[] NormalizeArguments(string[] arguments)
{
    return arguments switch
    {
        ["init", ..] => arguments,
        [_, "--", ..] => arguments,
        [var command, var subCommand, .. var remaining] => [command, "--", subCommand, .. remaining],
        _ => arguments,
    };
}

internal sealed class ScriptCommands(ScriptCommandRunnerOptions options)
{
    [Command("init")]
    public async Task<int> Init(bool force = false, CancellationToken cancellationToken = default)
    {
        var appSettingsPath = Path.GetFullPath(AppSettings.FileName);
        var appSettings = new AppSettings();

        try
        {
            var options = new FileStreamOptions
            {
                Mode = force ? FileMode.Create : FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            await using var stream = new FileStream(appSettingsPath, options);
            var context = AppSettingsJsonSerializerContext.Default.AppSettings;
            await JsonSerializer.SerializeAsync(stream, appSettings, context, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (IOException) when (!force && File.Exists(appSettingsPath))
        {
            Console.Error.WriteLine($"File already exists: {appSettingsPath}");
            Console.Error.WriteLine("Use --force to overwrite it.");
            return (int)ExitCode.Error;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to create {appSettingsPath}: {exception.Message}");
            return (int)ExitCode.Error;
        }

        Console.WriteLine($"Created: {appSettingsPath}");
        return (int)ExitCode.Success;
    }

    [Command("")]
    public Task<int> Run([Argument] string command, ConsoleAppContext context, CancellationToken cancellationToken)
    {
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

        if (FindScriptPath(applicationDirectory, options.ScriptDirectory, scriptName) is not { } scriptPath)
        {
            Console.Error.WriteLine($"Command not found: {command}");
            WriteCheckedScriptPaths(applicationDirectory, options.ScriptDirectory, scriptName);
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
            RedirectStandardError = true,
            RedirectStandardOutput = true,
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

            await Task.WhenAll(
                process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput(), cancellationToken),
                process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), cancellationToken),
                process.WaitForExitAsync(cancellationToken)
            );

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

    private static string? FindScriptPath(
        string applicationDirectory,
        ReadOnlySpan<string> scriptDirectories,
        string scriptName)
    {
        foreach (var scriptDirectory in scriptDirectories)
        {
            var candidatePath = GetScriptPath(applicationDirectory, scriptDirectory, scriptName);

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static void WriteCheckedScriptPaths(
        string applicationDirectory,
        ReadOnlySpan<string> scriptDirectories,
        string scriptName)
    {
        foreach (var scriptDirectory in scriptDirectories)
        {
            Console.Error.WriteLine($"  {GetScriptPath(applicationDirectory, scriptDirectory, scriptName)}");
        }
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
