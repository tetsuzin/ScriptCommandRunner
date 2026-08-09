using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ConsoleAppFramework;
using ScriptCommandRunner.Options;

namespace ScriptCommandRunner;

internal sealed class ScriptCommands(ScriptCommandRunnerOptions options)
{
    public async Task<int> Init(bool force = false, CancellationToken cancellationToken = default)
    {
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, AppSettings.FileName);

        if (!force && File.Exists(appSettingsPath))
        {
            Console.Error.WriteLine($"File already exists: {appSettingsPath}");
            Console.Error.WriteLine("Use --force to overwrite it.");
            return 1;
        }

        var temporaryPath = $"{appSettingsPath}.tmp";

        try
        {
            var context = AppSettingsJsonSerializerContext.Default.AppSettings;
            var json = JsonSerializer.Serialize(new AppSettings(), context);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, appSettingsPath, overwrite: force);
        }
        catch (IOException) when (!force && File.Exists(appSettingsPath))
        {
            TryDelete(temporaryPath);
            Console.Error.WriteLine($"File already exists: {appSettingsPath}");
            Console.Error.WriteLine("Use --force to overwrite it.");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            Console.Error.WriteLine($"Failed to create {appSettingsPath}: {exception.Message}");
            return 1;
        }

        Console.WriteLine($"Created: {appSettingsPath}");
        return 0;

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
            return Task.FromResult(1);
        }

        if (!IsValidCommandName(command))
        {
            Console.Error.WriteLine($"Invalid command name: {command}");
            return Task.FromResult(1);
        }

        static bool IsValidCommandName(string command) =>
            command is not ("" or "." or "..") && Path.GetFileName(command) == command;

        var applicationDirectory = AppContext.BaseDirectory;
        var scriptName = $"{command}{options.ScriptExtension}";
        var configuredDirectory = Path.IsPathRooted(options.ScriptDirectory)
            ? options.ScriptDirectory
            : Path.Combine(applicationDirectory, options.ScriptDirectory);
        var scriptPath = Path.Combine(Path.GetFullPath(configuredDirectory), scriptName);

        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"Command not found: {command}");
            Console.Error.WriteLine($"  {scriptPath}");
            return Task.FromResult(1);
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
            return 1;
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
}
