using System.Diagnostics;

namespace Tornado.Services;

public interface IKubectlService
{
    Task<KubectlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct);
}

public sealed class KubectlService : IKubectlService
{
    public async Task<KubectlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var command = "kubectl";

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                stopwatch.Stop();
                var finishedAtEarly = DateTimeOffset.UtcNow;
                return new KubectlResult(
                    command,
                    arguments,
                    -1,
                    string.Empty,
                    "Failed to start kubectl process.",
                    stopwatch.ElapsedMilliseconds,
                    startedAt,
                    finishedAtEarly
                );
            }

            using var registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore kill exceptions during cancellation.
                }
            });

            var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stdErrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var stdout = await stdOutTask;
            var stderr = await stdErrTask;

            stopwatch.Stop();
            var finishedAt = DateTimeOffset.UtcNow;

            return new KubectlResult(
                command,
                arguments,
                process.ExitCode,
                stdout,
                stderr,
                stopwatch.ElapsedMilliseconds,
                startedAt,
                finishedAt
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var finishedAt = DateTimeOffset.UtcNow;

            return new KubectlResult(
                command,
                arguments,
                -1,
                string.Empty,
                ex.ToString(),
                stopwatch.ElapsedMilliseconds,
                startedAt,
                finishedAt
            );
        }
    }
}

public sealed record KubectlResult(
    string Command,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    long DurationMs,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt
);
