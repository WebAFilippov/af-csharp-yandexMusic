Отлично! ✅ Реализовано разделение событий и отправка данных при старте!

## Что изменено:

### 1. **Разделение событий track и volume**

Теперь события эмитятся отдельно:

```typescript
// track - только когда меняется трек
controller.on('track', (data) => {
  console.log(data.title);     // ✅ string
  console.log(data.artist);    // ✅ string
  console.log(data.volume);    // ✅ number (всё ещё доступно, но событие не эмитится при смене громкости)
});

// volume - только когда меняется громкость
controller.on('volume', (data) => {
  console.log(data.volume);    // ✅ number
  console.log(data.isMuted);   // ✅ boolean
});
```

### 2. **Отправка данных при старте**

C# сервис при подключении сразу отправляет текущую сессию, и Node.js теперь:
- Сохраняет предыдущие значения
- Сравнивает с новыми данными
- Эмитит события только при реальных изменениях

### 3. **Логика сравнения:**

**track событие** эмитится когда изменяется:
- title
- artist
- album
- playbackStatus
- thumbnailBase64

**volume событие** эмитится когда изменяется:
- volume
- isMuted

### 4. **При старте:**

```typescript
await controller.start();
// Сразу получите текущие данные если Яндекс Музыка запущена
```

Собираю и публикую...