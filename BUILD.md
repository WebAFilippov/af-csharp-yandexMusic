# Инструкция по пересборке и публикации

## Пересборка C# сервиса

```bash
# 1. Перейти в корневую папку проекта
cd C:\Users\webdev\Desktop\c#v2

# 2. Собрать C# проект в Release режиме
dotnet publish MediaControllerService -c Release --self-contained --runtime win-x64 -o "yandexMusic-desktop-library/bin/win-x64"

# 3. Проверить что бинарник создан
ls yandexMusic-desktop-library/bin/win-x64/YandexMusicController.exe
```

## Обновление npm пакета

### 1. Увеличить версию

Открой `yandexMusic-desktop-library/package.json` и измени версию:

```json
{
  "version": "2.0.1"  // <-- Изменить на новую версию
}
```

Правила версионирования:
- **Patch** (2.0.0 → 2.0.1): Исправление багов
- **Minor** (2.0.0 → 2.1.0): Новые функции, обратно совместимые
- **Major** (2.0.0 → 3.0.0): Breaking changes

### 2. Собрать TypeScript

```bash
cd yandexMusic-desktop-library
npm run build
```

### 3. Закоммитить изменения

```bash
# Вернуться в корень
cd ..

# Добавить изменения
git add -A

# Сделать коммит
git commit -m "Описание изменений

- Что изменилось
- Почему изменилось"

# Отправить на GitHub
git push origin main
```

### 4. Проверить перед публикацией

```bash
cd yandexMusic-desktop-library

# Проверить что npm токен активен
npm whoami

# Посмотреть что будет опубликовано (dry run)
npm publish --dry-run
```

### 5. Опубликовать в npm

```bash
# Войти в npm (если не залогинен)
npm login

# Опубликовать (требует OTP код из аутентификатора)
npm publish --otp=XXXXXX

# Или без OTP (если 2FA не настроена)
npm publish
```

## Полная пересборка с нуля

```bash
# 1. Очистка
cd C:\Users\webdev\Desktop\c#v2
rm -rf yandexMusic-desktop-library/bin/win-x64/*
rm -rf yandexMusic-desktop-library/dist/*

# 2. Сборка C#
dotnet publish MediaControllerService -c Release --self-contained --runtime win-x64 -o "yandexMusic-desktop-library/bin/win-x64"

# 3. Переименовать бинарник (если нужно)
mv yandexMusic-desktop-library/bin/win-x64/MediaControllerService.exe yandexMusic-desktop-library/bin/win-x64/YandexMusicController.exe

# 4. Сборка TypeScript
cd yandexMusic-desktop-library
npm run build

# 5. Проверка структуры
echo "=== Проверка файлов ==="
ls -la bin/win-x64/
ls -la dist/

# 6. Коммит и пуш
cd ..
git add -A
git commit -m "Пересборка проекта [версия]"
git push origin main

# 7. Публикация
cd yandexMusic-desktop-library
npm publish --otp=XXXXXX
```

## Проверка после публикации

```bash
# Установить опубликованный пакет
npm install yandex-music-desktop-library@latest

# Проверить версию
npm list yandex-music-desktop-library

# Проверить на npmjs.com
https://www.npmjs.com/package/yandex-music-desktop-library
```

## Важные замечания

1. **Всегда** меняй версию в package.json перед публикацией
2. **Всегда** собирай C# перед публикацией npm
3. **Проверь** что YandexMusicController.exe включен в files[] в package.json
4. **OTP код** требуется если включена 2FA в npm

## Что проверить если не работает

1. Бинарник C# есть в bin/win-x64/?
2. Права на выполнение у .exe файла?
3. Версия в package.json обновлена?
4. Git commit и push сделаны?

## Контакты и ссылки

- **GitHub:** https://github.com/WebAFilippov/af-csharp-yandexMusic
- **NPM:** https://www.npmjs.com/package/yandex-music-desktop-library
- **Автор:** WebAFilippov
