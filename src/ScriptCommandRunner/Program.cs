using ConsoleAppFramework;
using ScriptCommandRunner.Options;
using System.Diagnostics;
using System.Text.Json;

var app = ConsoleApp.Create()
    .ConfigureDefaultConfiguration()
    .ConfigureServices();

app.Add<ScriptCommands>();
await app.RunAsync(NormalizeArguments(args));

static string[] NormalizeArguments(string[] arguments)
{
    return arguments switch
    {
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
        var json = JsonSerializer.Serialize(
            appSettings,
            AppSettingsJsonSerializerContext.Default.AppSettings);

        try
        {
            await using var stream = new FileStream(
                appSettingsPath,
                force ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json + Environment.NewLine);
            await writer.FlushAsync(cancellationToken);
        }
        catch (IOException) when (!force && File.Exists(appSettingsPath))
        {
            Console.Error.WriteLine($"File already exists: {appSettingsPath}");
            Console.Error.WriteLine("Use --force to overwrite it.");
            return 1;
        }

        Console.WriteLine($"Created: {appSettingsPath}");
        return 0;
    }

    [Command("")]
    public Task<int> Run([Argument] string command, ConsoleAppContext context, CancellationToken cancellationToken)
    {
        if (!IsValidCommandName(command))
        {
            Console.Error.WriteLine($"Invalid command name: {command}");
            return Task.FromResult(1);
        }

        var applicationDirectory = AppContext.BaseDirectory;
        var runnerOptions = options;
        var scriptName = $"{command}{runnerOptions.ScriptExtension}";
        var checkedScriptPaths = new Span<string>(new string[runnerOptions.ScriptDirectory.Length]);
        string? scriptPath = null;

        foreach (var scriptDirectory in runnerOptions.ScriptDirectory)
        {
            var resolvedDirectory = GetScriptDirectory(applicationDirectory, scriptDirectory);
            var candidatePath = Path.Combine(resolvedDirectory, scriptName);
            checkedScriptPaths.Add(candidatePath);

            if (File.Exists(candidatePath))
            {
                scriptPath = candidatePath;
                break;
            }
        }

        if (scriptPath is null)
        {
            Console.Error.WriteLine($"Command not found: {command}");
            foreach (var checkedScriptPath in checkedScriptPaths)
            {
                Console.Error.WriteLine($"  {checkedScriptPath}");
            }

            return Task.FromResult(1);
        }

        var startInfo = CreateStartInfo(
            applicationDirectory,
            runnerOptions.Executable,
            runnerOptions.ExecutableArguments,
            scriptPath,
            context.EscapedArguments);

        return RunProcessAsync(startInfo, scriptPath, cancellationToken);
    }

    private static ProcessStartInfo CreateStartInfo(
        string applicationDirectory,
        string executable,
        ReadOnlySpan<string> executableArguments,
        string scriptPath,
        ReadOnlySpan<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = applicationDirectory,
        };

        foreach (var argument in executableArguments)
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
                process.StandardOutput.BaseStream.CopyToAsync(
                    Console.OpenStandardOutput(),
                    cancellationToken),
                process.StandardError.BaseStream.CopyToAsync(
                    Console.OpenStandardError(),
                    cancellationToken),
                process.WaitForExitAsync(cancellationToken)
            );

            return process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Failed to start {scriptPath}: {exception.Message}");
            return 1;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The process has already exited or cannot be terminated.
        }
    }

    private static bool IsValidCommandName(string command)
    {
        return command switch
        {
            null or "" or "." or ".." => false,
            _ => !command.Contains('/') && !command.Contains('\\')
        };
    }

    private static string GetScriptDirectory(string applicationDirectory, string scriptDirectory)
    {
        var configuredDirectory = Path.IsPathRooted(scriptDirectory)
            ? scriptDirectory
            : Path.Combine(applicationDirectory, scriptDirectory);

        return Path.GetFullPath(configuredDirectory);
    }
}
