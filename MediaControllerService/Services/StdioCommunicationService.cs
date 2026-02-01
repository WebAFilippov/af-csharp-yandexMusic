using MediaControllerService.Models;
using System.Text;
using System.Text.Json;

namespace MediaControllerService.Services;

public class StdioCommunicationService : IDisposable
{
    private readonly MediaWatcherService _mediaWatcher;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _readTask;

    public event EventHandler? OnClientDisconnected;
    public event EventHandler<string>? OnCloseRequested;

    public StdioCommunicationService(MediaWatcherService mediaWatcher)
    {
        _mediaWatcher = mediaWatcher;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void Start()
    {
        Console.WriteLine("[Stdio] Communication service started");
        
        _mediaWatcher.OnSessionChanged += MediaWatcher_OnSessionChanged;
        _mediaWatcher.OnSessionUpdated += MediaWatcher_OnSessionUpdated;

        // Send initial session
        var session = _mediaWatcher.GetCurrentSession();
        SendSession(session);

        // Start reading from stdin
        _readTask = Task.Run(ReadStdinAsync);
    }

    private async Task ReadStdinAsync()
    {
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                
                if (line == null)
                {
                    // EOF - parent process closed stdin
                    Console.WriteLine("[Stdio] stdin closed, shutting down...");
                    OnClientDisconnected?.Invoke(this, EventArgs.Empty);
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                await ProcessMessageAsync(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stdio] Error reading from stdin: {ex.Message}");
            OnClientDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            var command = JsonSerializer.Deserialize<CommandMessage>(message);

            if (command == null)
                return;

            if (command.Command.ToLowerInvariant() == "close")
            {
                OnCloseRequested?.Invoke(this, command.Command);
                return;
            }

            // For Yandex Music, we ignore sessionId and always control the active session
            var success = await _mediaWatcher.SendCommandAsync(command.Command, command.StepPercent);

            if (!success)
            {
                await SendErrorAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stdio] Error processing message: {ex.Message}");
            await SendErrorAsync();
        }
    }

    private void MediaWatcher_OnSessionChanged(object? sender, MediaSessionDto? session)
    {
        SendSession(session);
    }

    private void MediaWatcher_OnSessionUpdated(object? sender, MediaSessionDto? session)
    {
        SendSession(session);
    }

    private void SendSession(MediaSessionDto? session)
    {
        try
        {
            var message = new Message
            {
                Type = "session",
                Data = session
            };
            
            var json = JsonSerializer.Serialize(message);
            Console.WriteLine(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stdio] Error sending message: {ex.Message}");
        }
    }

    private Task SendErrorAsync()
    {
        SendSession(null);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _readTask?.Wait(TimeSpan.FromSeconds(2));
        _cancellationTokenSource.Dispose();
    }
}
