Отлично! ✅ **Версия 2.0.0** готова и запушена!

## Что сделано:

### Перенос логики на C#:

**Раньше (Node.js):**
- C# отправлял всё вместе в `type: "session"`
- Node.js фильтровал и разделял события
- Дублирование логики

**Теперь (C#):**
- C# отправляет `type: "media"` только при изменении трека
- C# отправляет `type: "volume"` только при изменении громкости
- Node.js просто проксирует события

### Новый протокол:

```json
// type: "media" - только данные трека
{
  "type": "media",
  "data": {
    "id": "...",
    "title": "Song Name",
    "artist": "Artist",
    "album": "Album",
    "playbackStatus": "Playing",
    "thumbnailBase64": "...",
    "isFocused": true
  }
}

// type: "volume" - только громкость
{
  "type": "volume",
  "data": {
    "volume": 75,
    "isMuted": false
  }
}
```

### Обновленный API Node.js:

```typescript
import ymc from 'yandex-music-desktop-library';

const controller = new ymc();

// Теперь 'media' вместо 'track'
controller.on('media', (data) => {
  console.log(data.title, data.artist); // ✅ только трек
});

// 'volume' отдельно
controller.on('volume', (data) => {
  console.log(data.volume, data.isMuted); // ✅ только громкость
});
```

## Коммиты:
- `29 files changed` - основные изменения протокола
- `version 2.0.0` - major bump (breaking change)

**GitHub:** https://github.com/WebAFilippov/yandexMusic-desktop-library

Для публикации:
```bash
cd yandexMusic-desktop-library
npm publish
```

⚠️ **Breaking Change**: Событие `'track'` переименовано в `'media'`!

🎵 Готово!