using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FolderNotifier;

// The mutex owns the instance; this per-user pipe only forwards explicit opens.
internal sealed class InstanceActivation : IDisposable
{
    internal static string PipeName => $"FolderNotifier.Desktop.{Process.GetCurrentProcess().SessionId}";
    private readonly CancellationTokenSource stopping = new();
    private readonly Task listener;

    internal InstanceActivation(Action activate, string? pipeName = null)
    {
        listener = ListenAsync(pipeName ?? PipeName, activate);
    }

    private async Task ListenAsync(string pipeName, Action activate)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                var command = new byte[1];
                using var request = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                request.CancelAfter(TimeSpan.FromSeconds(2));
                if (await pipe.ReadAsync(command, request.Token).ConfigureAwait(false) == 1 && command[0] == 1)
                    activate();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    internal static async Task<bool> RequestAsync(string? pipeName = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName,
                PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            // A shortcut launch has foreground permission. Transfer it before
            // asking the existing process to activate its WPF window.
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
                AllowSetForegroundWindow(processId);
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.GetAwaiter().GetResult();
        stopping.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
