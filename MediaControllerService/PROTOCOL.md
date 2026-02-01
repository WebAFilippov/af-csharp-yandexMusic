# MediaControllerService Protocol Documentation

**Версия для Яндекс Музыки (Yandex Music Focused)**

Приложение теперь работает ТОЛЬКО с Яндекс Музыкой и фильтрует другие медиа-сессии.

## Communication

Общение через **child_process stdin/stdout** (JSON сообщения, каждое на новой строке).

### Connection Flow

1. Electron запускает C# процесс:
   ```javascript
   const { spawn } = require('child_process');
   const service = spawn('MediaControllerService.exe');
   ```

2. C# читает команды из stdin, пишет события в stdout

3. Реальное время: данные приходят автоматически при изменениях

### Message Types

#### 1. Session Update (C# → Node.js)

Отправляется при:
- Запуске Яндекс Музыки
- Смене трека
- Изменении статуса (play/pause)
- Закрытии приложения

```json
{
  "type": "session",
  "data": {
    "id": "a1b2c3d4e5f6...",
    "appId": "Яндекс Музыка.exe",
    "appName": "Яндекс Музыка",
    "title": "Название трека",
    "artist": "Исполнитель",
    "album": "Альбом",
    "playbackStatus": "Playing",
    "thumbnailBase64": "/9j/4AAQSkZJRgABAQAAAQ...",
    "isFocused": true,
    "volume": 75,
    "isMuted": false
  }
}
```

Или `null` если Яндекс Музыка не запущена или нет активного трека:
```json
{
  "type": "session",
  "data": null
}
```

#### 2. Command (Node.js → C#)

Управление плеером:

```json
{
  "command": "playpause"
}
```

**Доступные команды:**
- `play` - Воспроизведение
- `pause` - Пауза
- `playpause` или `toggle` - Переключить play/pause
- `next` или `nexttrack` - Следующий трек
- `previous`, `prev`, или `prevtrack` - Предыдущий трек
- `close` - Завершить C# сервис

**Важно:** `sessionId` не требуется - команды всегда управляют Яндекс Музыкой.

**Дополнительные команды для громкости:**
- `volume_up` - Увеличить громкость
- `volume_down` - Уменьшить громкость
- `toggle_mute` - Переключить mute

С опциональным параметром `stepPercent`:
```json
{
  "command": "volume_up",
  "stepPercent": 5
}
```

По умолчанию шаг 3%, но можно указать любое значение 1-100.

### Field Descriptions

- `id` - Уникальный GUID сессии
- `appId` - Идентификатор приложения (всегда "Яндекс Музыка.exe")
- `appName` - Имя приложения (всегда "Яндекс Музыка")
- `title` - Название трека (пустая строка = не отправляем данные)
- `artist` - Исполнитель
- `album` - Альбом
- `playbackStatus` - "Playing", "Paused", или "Stopped"
- `thumbnailBase64` - JPEG 150x150 в base64
- `isFocused` - Всегда true для Яндекс Музыки
- `volume` - Текущий уровень громкости Windows (0-100)
- `isMuted` - Состояние mute (true/false)

### Important Notes

1. **Фильтр по title**: Данные отправляются только если `title` не пустой
2. **Только Яндекс Музыка**: Все другие медиа-приложения игнорируются
3. **Кэширование обложек**: Обложки кэшируются по Artist+Album
4. **Graceful shutdown**: Отправьте `{"command":"close"}` для завершения
5. **Громкость и mute управляются глобально для Windows** (не только Яндекс Музыка)
6. **Изменения громкости отслеживаются в реальном времени** (обновление каждые 200ms)
7. **При изменении громкости с клавиатуры или другими способами**, обновление придет автоматически

## Electron Integration Example

```javascript
const { spawn } = require('child_process');
const readline = require('readline');

// Start C# service
const service = spawn('MediaControllerService.exe');

// Read JSON messages from stdout
const rl = readline.createInterface({
  input: service.stdout,
  crlfDelay: Infinity
});

rl.on('line', (line) => {
  try {
    const msg = JSON.parse(line);
    
    if (msg.type === 'session') {
      if (msg.data) {
        console.log('Now playing:', msg.data.title, 'by', msg.data.artist);
        // Send to ESP32 via MQTT
      } else {
        console.log('Yandex Music not active');
      }
    }
  } catch (e) {
    console.log('Debug:', line);
  }
});

// Send commands (no sessionId needed!)
function sendCommand(command) {
  service.stdin.write(JSON.stringify({ command }) + '\n');
}

// Control playback
sendCommand('playpause');  // Toggle play/pause
sendCommand('next');       // Next track
sendCommand('previous');   // Previous track

// Shutdown
process.on('exit', () => {
  service.stdin.write(JSON.stringify({ command: 'close' }) + '\n');
  service.kill();
});
```

## MQTT Integration for ESP32

```javascript
const mqtt = require('mqtt');
const client = mqtt.connect('mqtt://esp32-ip');

rl.on('line', (line) => {
  const msg = JSON.parse(line);
  if (msg.type === 'session' && msg.data) {
    // Send to ESP32
    client.publish('media/current', JSON.stringify({
      title: msg.data.title,
      artist: msg.data.artist,
      status: msg.data.playbackStatus,
      hasThumbnail: !!msg.data.thumbnailBase64
    }));
    
    // Send thumbnail separately (large data)
    if (msg.data.thumbnailBase64) {
      client.publish('media/thumbnail', msg.data.thumbnailBase64);
    }
  }
});

// Receive commands from ESP32
client.subscribe('esp32/commands');
client.on('message', (topic, message) => {
  const cmd = message.toString();
  sendCommand(cmd);  // play, pause, next, prev
});
```

## Build Instructions

```bash
# Build
dotnet build MediaControllerService

# Publish single-file executable
dotnet publish MediaControllerService --configuration Release --self-contained --runtime win-x64

# Output:
# MediaControllerService/bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/MediaControllerService.exe
```

## Project Structure

```
MediaControllerService/
├── Program.cs                      # Entry point
├── MediaControllerService.csproj   # Project config
├── PROTOCOL.md                     # This documentation
├── Models/
│   ├── MediaSessionDto.cs          # Session data model
│   ├── WebSocketMessage.cs         # Message models
│   └── ThumbnailCacheKey.cs        # Cache key
└── Services/
    ├── MediaWatcherService.cs      # Yandex Music filter
    ├── StdioCommunicationService.cs # stdin/stdout communication
    └── ThumbnailService.cs         # Image processing
```

## Dependencies

- **Dubya.WindowsMediaController** (2.5.6) - Windows Media API
- **SkiaSharp** (2.88.8) - Image processing (JPEG 150x150)
- **System.Text.Json** (8.0.5) - JSON serialization

## Changes from Generic Version

1. **Убран WebSocket** → заменен на child_process stdin/stdout
2. **Фильтр приложений** → только `session.Id == "Яндекс Музыка.exe"`
3. **Фильтр данных** → отправляем только если `title != ""`
4. **Убран sessionId** → команды всегда для Яндекс Музыки
5. **Single session** → вместо массива сессий передаем одну

## Testing

```bash
cd MediaControllerTest
npm run test  # or: node yandex-test.js
```
