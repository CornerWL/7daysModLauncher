using System.IO;
using System.IO.Pipes;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Один экземпляр приложения: второй запуск (клик nxm://) пересылает ссылку
/// первому через named pipe и молча выходит.
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = @"Local\7daysModLauncher.SingleInstance";
    private const string PipeName = "7daysModLauncher.NxmPipe";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _listenerCts;

    /// <summary>true — мы первые, работаем дальше. false — уже запущено, ссылка переслана.</summary>
    public static bool TryAcquireOrForward(string? nxmLink)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
            return true;

        // Уже запущено — пересылаем ссылку и выходим
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(nxmLink ?? "");
            AppLogger.Info("Forwarded nxm link to running instance");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Forward nxm failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>Слушает ссылки от вторых экземпляров. Вызывать только в первом.</summary>
    public static void StartListener(Action<string> onNxmLink)
    {
        _listenerCts?.Cancel();
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var line = (await reader.ReadLineAsync(token))?.Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        AppLogger.Info($"Received nxm link from second instance: {line}");
                        onNxmLink(line);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Pipe listener error: {ex.Message}");
                    await Task.Delay(500, token).ContinueWith(_ => { });
                }
            }
        }, token);
    }

    public static void Stop()
    {
        try { _listenerCts?.Cancel(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
    }
}
