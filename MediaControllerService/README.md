# YandexMusicController

Сервис для интеграции Яндекс Музыки с внешними устройствами через stdin/stdout.

## Описание

YandexMusicController - это Windows-приложение на C#, которое отслеживает воспроизведение Яндекс Музыки и предоставляет API для управления через стандартные потоки ввода/вывода (stdin/stdout). Приложение передает метаданные текущего трека (название, исполнитель, обложка) и позволяет управлять воспроизведением и громкостью.

## Возможности

- 🔍 Автоматическое обнаружение Яндекс Музыки
- 📊 Передача метаданных трека в реальном времени
- 🖼️ Конвертация обложек в формат 150x150 JPEG
- 🎵 Управление воспроизведением (play, pause, next, prev)
- 🔊 Управление громкостью Windows (volume_up, volume_down, set_volume)
- 🔇 Управление mute (toggle_mute)
- 📡 Обмен данными через JSON по stdin/stdout

## Быстрый старт

### Сборка

```bash
# Клонирование репозитория
git clone <repository-url>
cd MediaControllerService

# Сборка
dotnet build

# Публикация как single-file executable
dotnet publish -c Release --self-contained --runtime win-x64

# Исполняемый файл будет находиться по пути:
# bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/YandexMusicController.exe
```

### Запуск

```bash
# Прямой запуск
YandexMusicController.exe

# Запуск из Node.js/Electron
const { spawn } = require('child_process');
const service = spawn('YandexMusicController.exe');
```

## API Documentation

### Communication Protocol

Приложение использует stdin/stdout для обмена JSON-сообщениями. Каждое сообщение находится на отдельной строке.

#### Исходящие сообщения (C# → Node.js)

Приложение отправляет обновления при:
- Запуске Яндекс Музыки
- Смене трека
- Изменении статуса воспроизведения
- Изменении громкости
- Закрытии приложения

**Формат сообщения:**
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

Или `null` если Яндекс Музыка не запущена:
```json
{
  "type": "session",
  "data": null
}
```

**Поля:**
- `id` - уникальный идентификатор сессии
- `title` - название трека (отправляется только если не пустое)
- `artist` - исполнитель
- `album` - альбом
- `playbackStatus` - "Playing", "Paused" или "Stopped"
- `thumbnailBase64` - обложка альбома в формате JPEG 150x150, base64
- `volume` - текущая громкость Windows (0-100)
- `isMuted` - состояние mute (true/false)

#### Входящие команды (Node.js → C#)

**Управление воспроизведением:**
```json
{"command": "play"}
{"command": "pause"}
{"command": "playpause"}
{"command": "next"}
{"command": "previous"}
```

**Управление громкостью:**
```json
{"command": "volume_up", "stepPercent": 5}
{"command": "volume_down", "stepPercent": 5}
{"command": "set_volume", "value": 75}
{"command": "toggle_mute"}
```

**Завершение работы:**
```json
{"command": "close"}
```

**Параметры:**
- `stepPercent` - шаг изменения громкости (по умолчанию 3%)
- `value` - конкретное значение громкости для set_volume (0-100)

### Примеры использования

#### Node.js / Electron

```javascript
const { spawn } = require('child_process');
const readline = require('readline');

// Запуск сервиса
const service = spawn('YandexMusicController.exe');

// Чтение сообщений
const rl = readline.createInterface({
  input: service.stdout,
  crlfDelay: Infinity
});

rl.on('line', (line) => {
  const msg = JSON.parse(line);
  if (msg.type === 'session' && msg.data) {
    console.log('Now playing:', msg.data.title);
    console.log('Volume:', msg.data.volume + '%');
    console.log('Muted:', msg.data.isMuted);
  }
});

// Отправка команд
function sendCommand(cmd, params = {}) {
  service.stdin.write(JSON.stringify({ command: cmd, ...params }) + '\n');
}

// Управление
sendCommand('playpause');
sendCommand('volume_up', { stepPercent: 5 });
sendCommand('set_volume', { value: 50 });
sendCommand('toggle_mute');

// Завершение
process.on('exit', () => {
  service.stdin.write(JSON.stringify({ command: 'close' }) + '\n');
  service.kill();
});
```

#### MQTT интеграция с ESP32

```javascript
const mqtt = require('mqtt');
const client = mqtt.connect('mqtt://esp32-ip');

// Отправка данных на ESP32
rl.on('line', (line) => {
  const msg = JSON.parse(line);
  if (msg.type === 'session' && msg.data) {
    client.publish('media/current', JSON.stringify({
      title: msg.data.title,
      artist: msg.data.artist,
      volume: msg.data.volume,
      isMuted: msg.data.isMuted,
      status: msg.data.playbackStatus
    }));
    
    if (msg.data.thumbnailBase64) {
      client.publish('media/thumbnail', msg.data.thumbnailBase64);
    }
  }
});

// Получение команд от ESP32
client.subscribe('esp32/commands');
client.on('message', (topic, message) => {
  const cmd = message.toString();
  // cmd может быть: 'playpause', 'next', 'volume_up', 'volume_down', 'toggle_mute'
  sendCommand(cmd);
});
```

## Требования

- Windows 10/11 (версия 1809 или выше)
- .NET 8.0 SDK (для сборки)
- Яндекс Музыка (desktop приложение)

## Архитектура

```
YandexMusicController/
├── Program.cs                      # Точка входа
├── Models/
│   ├── MediaSessionDto.cs         # DTO для данных трека
│   ├── Message.cs                  # Модели сообщений
│   └── ThumbnailCacheKey.cs       # Ключ кэширования обложек
└── Services/
    ├── MediaWatcherService.cs     # Мониторинг Яндекс Музыки
    ├── AudioService.cs            # Управление громкостью Windows
    ├── StdioCommunicationService.cs # Коммуникация stdin/stdout
    └── ThumbnailService.cs        # Обработка изображений
```

## Зависимости

- [Dubya.WindowsMediaController](https://www.nuget.org/packages/Dubya.WindowsMediaController/) (2.5.6) - Windows Media API
- [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/) (2.88.8) - Обработка изображений
- System.Text.Json (8.0.5) - JSON сериализация

## Лицензия

MIT

## Вклад в проект

Pull requests приветствуются. Для крупных изменений, пожалуйста, сначала создайте issue для обсуждения.

## Поддержка

Если у вас есть вопросы или проблемы, пожалуйста, создайте issue в репозитории.
