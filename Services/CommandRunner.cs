using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OS_Concepts_Demonstrator.Services;

public record CommandResult(bool Started, int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// <summary>
/// Runs a fixed external command and captures its output. No shell is involved: the program
/// and each argument are passed separately, so text can never be interpreted as extra commands.
/// Callers must only pass read-only commands.
/// </summary>
public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // Plain output: no colours, no pager, no terminal tricks.
        startInfo.Environment["SYSTEMD_COLORS"] = "0";
        startInfo.Environment["SYSTEMD_PAGER"] = string.Empty;
        startInfo.Environment["TERM"] = "dumb";

        Process? process = null;

        try
        {
            process = Process.Start(startInfo);

            if (process is null)
                return new CommandResult(false, -1, string.Empty, "The command could not be started.", false);

            // The command must never wait for input.
            process.StandardInput.Close();

            using var cts = new CancellationTokenSource(timeout);

            // Read both streams at the same time so neither pipe can fill up and block the command.
            Task<string> output = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            return new CommandResult(true, process.ExitCode, await output, await error, false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new CommandResult(true, -1, string.Empty, "The command took too long and was stopped.", true);
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            return new CommandResult(false, -1, string.Empty, ex.Message, false);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
        {
            // Already gone.
        }
    }
}
