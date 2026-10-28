using System.Diagnostics;
using System.IO.Pipes;
using System.IO;
using System.Security.Principal;

namespace CatFish.Core;

// The mutex elects one owner; a pipe carries activation requests from later launches.
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    private bool _disposed;
    public bool IsPrimary { get; }
    public event Action? ActivationRequested;

    public SingleInstanceService(string? name = null)
    {
        _pipeName = name ?? $"CatFish.{WindowsIdentity.GetCurrent().User!.Value}.{Process.GetCurrentProcess().SessionId}";
        _mutex = new Mutex(false, @"Local\" + _pipeName);
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public void StartListening()
    {
        if (!IsPrimary || _listener is not null) return;
        _listener = ListenAsync(_stop.Token);
    }

    private async Task ListenAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(stop);
                request.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(Environment.ProcessId.ToString().AsMemory(), request.Token).ConfigureAwait(false);
                if (await reader.ReadLineAsync(request.Token).ConfigureAwait(false) == "activate")
                {
                    ActivationRequested?.Invoke();
                    await writer.WriteLineAsync("ok".AsMemory(), request.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { if (stop.IsCancellationRequested) break; }
            catch (IOException) { }
        }
    }

    public async Task<bool> NotifyExistingInstanceAsync()
    {
        if (IsPrimary) return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            if (!uint.TryParse(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), out var processId)) return false;
            // The newly launched process can grant its foreground permission to the tray instance.
            NativeMethods.AllowSetForegroundWindow(processId);
            await writer.WriteLineAsync("activate".AsMemory(), timeout.Token).ConfigureAwait(false);
            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "ok";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _listener?.GetAwaiter().GetResult(); }
        finally
        {
            _stop.Dispose();
            // Close the pipe before releasing ownership to a future launch.
            // The application creates and disposes this service on its UI thread.
            if (IsPrimary) _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
