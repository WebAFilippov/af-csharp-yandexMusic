Отлично! ✅ Исправлена проблема с начальными данными!

## Что было не так:

При старте контроллера:
- Отправлялась только громкость (volume)
- Media данные (трек, playbackStatus) не отправлялись!
- Событие `OnAnyMediaPropertyChanged` не вызывалось пока не изменятся метаданные

## Решение:

Теперь `SendInitialDataAsync` при старте:
1. Отправляет текущую громкость
2. **Получает текущие media свойства** через `_yandexSession.ControlSession.TryGetMediaPropertiesAsync()`
3. **Отправляет media данные** с текущим playbackStatus

## Код изменений:

```csharp
// Отправка громкости (как раньше)
var volumeData = new VolumeData { ... };
OnVolumeChanged?.Invoke(this, volumeData);

// НОВОЕ: Получаем и отправляем media данные
if (_yandexSession?.ControlSession != null)
{
    var playbackInfo = _yandexSession.ControlSession.GetPlaybackInfo();
    var mediaProperties = await _yandexSession.ControlSession.TryGetMediaPropertiesAsync();
    
    if (IsValidSessionData(mediaProperties.Title))
    {
        var mediaData = new MediaData {
            Title = mediaProperties.Title,
            Artist = mediaProperties.Artist,
            PlaybackStatus = playbackInfo.PlaybackStatus.ToString(),
            // ... остальные поля
        };
        OnMediaChanged?.Invoke(this, mediaData);
    }
}
```

## Результат:

Теперь при запуске контроллера вы сразу получаете:
- ✅ Текущий трек (media событие)
- ✅ Текущий playbackStatus (Playing/Paused)
- ✅ Текущую громкость (volume событие)

## Версия 2.0.2:

- **Коммит:** `91d8b54` / `0d0890c`
- **Статус:** Собрано и запушено
- **Действие:** Опубликуйте в npm:
  ```bash
  cd yandexMusic-desktop-library
  npm publish --otp=XXXXXX
  ```

Теперь слушатель работает сразу при старте! 🎵