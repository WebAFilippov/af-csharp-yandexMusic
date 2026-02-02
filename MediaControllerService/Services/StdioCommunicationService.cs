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
        
        _mediaWatcher.OnMediaChanged += MediaWatcher_OnMediaChanged;
        _mediaWatcher.OnVolumeChanged += MediaWatcher_OnVolumeChanged;
        _mediaWatcher.OnSessionClosed += MediaWatcher_OnSessionClosed;

        // Initial media data will be sent via OnMediaChanged event when session is detected

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

    private void MediaWatcher_OnMediaChanged(object? sender, MediaData? mediaData)
    {
        SendMessage(new Message { Type = "media", Data = mediaData });
    }

    private void MediaWatcher_OnVolumeChanged(object? sender, VolumeData volumeData)
    {
        SendMessage(new Message { Type = "volume", Data = volumeData });
    }

    private void MediaWatcher_OnSessionClosed(object? sender, EventArgs e)
    {
        SendMessage(new Message { Type = "media", Data = null });
    }

    private void SendMessage(Message message)
    {
        try
        {
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
        SendMessage(new Message { Type = "media", Data = null });
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _readTask?.Wait(TimeSpan.FromSeconds(2));
        _cancellationTokenSource.Dispose();
    }
}
