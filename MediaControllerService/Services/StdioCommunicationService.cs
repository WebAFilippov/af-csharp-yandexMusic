using MediaControllerService.Models;
using System.Text;
using System.Text.Json;

namespace MediaControllerService.Services;

public class StdioCommunicationService : IDisposable
{
    private readonly MediaWatcherService _mediaWatcher;
    private readonly AudioService _audioService;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _readTask;

    public event EventHandler? OnClientDisconnected;
    public event EventHandler<string>? OnCloseRequested;

    public StdioCommunicationService(MediaWatcherService mediaWatcher, AudioService audioService)
    {
        _mediaWatcher = mediaWatcher;
        _audioService = audioService;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void Start()
    {
        Console.WriteLine("[Stdio] Communication service started");
        
        // Media events from Yandex Music
        _mediaWatcher.OnMediaChanged += MediaWatcher_OnMediaChanged;
        _mediaWatcher.OnSessionClosed += MediaWatcher_OnSessionClosed;
        
        // Volume events from Windows Audio (independent of Yandex Music)
        _audioService.OnVolumeChanged += AudioService_OnVolumeChanged;
        _audioService.OnError += AudioService_OnError;
        
        // Send initial volume data immediately (list of all devices)
        var devices = _audioService.GetAllDevices();
        SendMessage(new Message { Type = "volume", Data = devices });

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
            SendError(new ErrorData
            {
                Code = "STDIN_READ_ERROR",
                Message = $"Failed to read from stdin: {ex.Message}"
            });
            OnClientDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            var command = JsonSerializer.Deserialize<CommandMessage>(message);

            if (command == null)
            {
                SendError(new ErrorData
                {
                    Code = "INVALID_COMMAND",
                    Message = "Failed to deserialize command message"
                });
                return;
            }

            if (command.Command.ToLowerInvariant() == "close")
            {
                OnCloseRequested?.Invoke(this, command.Command);
                return;
            }

            var commandLower = command.Command.ToLowerInvariant();
            
            switch (commandLower)
            {
                case "volume_up":
                    _audioService.VolumeUp(command.StepPercent ?? 3, command.DeviceId);
                    break;
                case "volume_down":
                    _audioService.VolumeDown(command.StepPercent ?? 3, command.DeviceId);
                    break;
                case "set_volume":
                case "setvolume":
                    if (command.Value.HasValue)
                    {
                        _audioService.SetVolume(command.Value.Value, command.DeviceId);
                        Console.WriteLine($"[Stdio] Volume set to {command.Value.Value}% on device '{command.DeviceId ?? "default"}'");
                    }
                    else
                    {
                        SendError(new ErrorData
                        {
                            Code = "MISSING_VALUE",
                            Message = "set_volume command requires 'value' parameter",
                            Details = new { Command = command.Command }
                        });
                    }
                    break;
                case "toggle_mute":
                    _audioService.ToggleMute(command.DeviceId);
                    break;
                case "mute":
                    _audioService.SetMute(true, command.DeviceId);
                    break;
                case "unmute":
                    _audioService.SetMute(false, command.DeviceId);
                    break;
                default:
                    // Media commands - route to Yandex Music
                    var success = await _mediaWatcher.SendCommandAsync(command.Command, command.StepPercent, command.Value);
                    if (!success)
                    {
                        SendError(new ErrorData
                        {
                            Code = "MEDIA_COMMAND_FAILED",
                            Message = $"Media command '{command.Command}' failed",
                            Details = new { Command = command.Command }
                        });
                    }
                    break;
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Stdio] JSON parsing error: {ex.Message}");
            SendError(new ErrorData
            {
                Code = "JSON_PARSE_ERROR",
                Message = $"Failed to parse JSON message: {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stdio] Error processing message: {ex.Message}");
            SendError(new ErrorData
            {
                Code = "PROCESSING_ERROR",
                Message = $"Error processing command: {ex.Message}"
            });
        }
    }

    private void MediaWatcher_OnMediaChanged(object? sender, MediaData? mediaData)
    {
        SendMessage(new Message { Type = "media", Data = mediaData });
    }

    private void AudioService_OnVolumeChanged(object? sender, List<AudioDevice> devices)
    {
        SendMessage(new Message { Type = "volume", Data = devices });
    }

    private void AudioService_OnError(object? sender, ErrorData error)
    {
        SendError(error);
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

    private void SendError(ErrorData error)
    {
        SendMessage(new Message { Type = "error", Data = error });
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _readTask?.Wait(TimeSpan.FromSeconds(2));
        _cancellationTokenSource.Dispose();
    }
}
