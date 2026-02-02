@echo off
REM Скрипт для полной пересборки проекта

echo === НАЧАЛО ПЕРЕСБОРКИ ===

REM 1. Переход в директорию проекта
cd /d C:\Users\webdev\Desktop\c#v2

REM 2. Очистка старых файлов
echo [1/7] Очистка старых бинарников...
if exist "yandexMusic-desktop-library\bin\win-x64\MediaControllerService.exe" del "yandexMusic-desktop-library\bin\win-x64\MediaControllerService.exe"
if exist "yandexMusic-desktop-library\bin\win-x64\MediaControllerService.pdb" del "yandexMusic-desktop-library\bin\win-x64\MediaControllerService.pdb"

REM 3. Сборка C# проекта
echo [2/7] Сборка C# проекта...
dotnet publish MediaControllerService -c Release --self-contained --runtime win-x64 -o "yandexMusic-desktop-library\bin\win-x64"
if errorlevel 1 (
    echo [ОШИБКА] Сборка C# проекта не удалась!
    pause
    exit /b 1
)

REM 4. Переименование бинарника (на всякий случай)
echo [3/7] Проверка бинарника...
if exist "yandexMusic-desktop-library\bin\win-x64\MediaControllerService.exe" (
    rename "yandexMusic-desktop-library\bin\win-x64\MediaControllerService.exe" "YandexMusicController.exe"
    echo [ИНФО] Бинарник переименован
)

REM 5. Сборка TypeScript
echo [4/7] Сборка TypeScript...
cd yandexMusic-desktop-library
npm run build
if errorlevel 1 (
    echo [ОШИБКА] Сборка TypeScript не удалась!
    pause
    exit /b 1
)

REM 6. Проверка структуры
echo [5/7] Проверка структуры...
echo Проверка C# бинарника:
if exist "bin\win-x64\YandexMusicController.exe" (
    echo [OK] YandexMusicController.exe найден
) else (
    echo [ОШИБКА] YandexMusicController.exe НЕ НАЙДЕН!
    pause
    exit /b 1
)

echo Проверка dist:
if exist "dist\index.js" (
    echo [OK] dist/index.js найден
) else (
    echo [ОШИБКА] dist/index.js НЕ НАЙДЕН!
    pause
    exit /b 1
)

REM 7. Git коммит и пуш
echo [6/7] Git commit и push...
cd ..
git add -A
git commit -m "Пересборка проекта $(date)"
git push origin main

echo [7/7] ГОТОВО!
echo.
echo === ИТОГИ ===
echo - C# бинарник собран: bin/win-x64/YandexMusicController.exe
echo - TypeScript собран: dist/
echo - Изменения отправлены на GitHub
echo.
echo Для публикации в npm выполните:
echo   cd yandexMusic-desktop-library
echo   npm publish --otp=XXXXXX
echo.
pause
