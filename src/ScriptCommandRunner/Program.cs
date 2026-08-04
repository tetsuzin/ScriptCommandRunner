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
            return 1;
        }

        Console.WriteLine($"Created: {appSettingsPath}");
        return 0;
    }

    [Command("")]
    public Task<int> Run([Argument] string command, ConsoleAppContext context, CancellationToken cancellationToken)
    {
        if (!isValidCommandName(command))
        {
            Console.Error.WriteLine($"Invalid command name: {command}");
            return Task.FromResult(1);
        }

        static bool isValidCommandName(string command) => command switch
        {
            null or "" or "." or ".." => false,
            _ => !command.Contains('/') && !command.Contains('\\')
        };

        var applicationDirectory = AppContext.BaseDirectory;
        var scriptName = $"{command}{options.ScriptExtension}";

        if (FetchScriptPath(applicationDirectory, options.ScriptDirectory, scriptName) is not { } scriptPath)
        {
            Console.Error.WriteLine($"Command not found: {command}");
            return Task.FromResult(1);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = options.Executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = applicationDirectory,
        };

        foreach (var argument in options.ExecutableArguments) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in context.EscapedArguments) startInfo.ArgumentList.Add(argument);

        return RunProcessAsync(startInfo, scriptPath, cancellationToken);
    }

    private static async Task<int> RunProcessAsync(ProcessStartInfo startInfo, string scriptPath, CancellationToken cancellationToken)
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
            tryKill(process);
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Failed to start {scriptPath}: {exception.Message}");
            return 1;
        }

        static void tryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // The process has already exited or cannot be terminated.
            }
        }
    }

    private static string FetchScriptPath(string applicationDirectory, string[] scriptDirectory, string scriptName)
    {
        foreach (var dir in scriptDirectory)
        {
            var candidatePath = getScriptPath(applicationDirectory, dir, scriptName);

            if (File.Exists(candidatePath))
                return candidatePath;
        }

        throw new FileNotFoundException($"Script not found: {scriptName}");

        static string getScriptPath(string applicationDirectory, string scriptDirectory, string scriptName)
        {
            var resolvedDirectory = getScriptDirectory(applicationDirectory, scriptDirectory);
            return Path.Combine(resolvedDirectory, scriptName);
        }

        static string getScriptDirectory(string applicationDirectory, string scriptDirectory)
        {
            var configuredDirectory = Path.IsPathRooted(scriptDirectory)
                ? scriptDirectory
                : Path.Combine(applicationDirectory, scriptDirectory);

            return Path.GetFullPath(configuredDirectory);
        }
    }
}
