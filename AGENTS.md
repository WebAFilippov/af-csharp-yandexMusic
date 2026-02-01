# AGENTS.md - MediaWatcher Project Guidelines

## Project Overview
C# console application that monitors Windows media playback sessions using the WindowsMediaController library. Monitors media events (playback state, track changes, timeline updates) and logs them to console.

## Build Commands

```bash
# Build the project
dotnet build

# Build specific configuration
dotnet build --configuration Release
dotnet build --configuration Debug

# Publish as single self-contained executable
dotnet publish --configuration Release --self-contained --runtime win-x64

# Restore dependencies
dotnet restore

# Clean build artifacts
dotnet clean
```

## Code Style Guidelines

### Formatting
- Use 4 spaces for indentation
- Opening brace on same line (K&R style)
- Max line length: 120 characters
- Use `var` when type is obvious from right side

### Naming Conventions
- **Classes/Interfaces**: PascalCase (e.g., `MediaManager`)
- **Methods**: PascalCase (e.g., `OnSessionOpened`)
- **Private fields**: camelCase with underscore prefix (e.g., `_writeLock`)
- **Static readonly**: PascalCase (e.g., `WriteLock`)
- **Local variables**: camelCase
- **Constants**: PascalCase

### Imports & Using Statements
- Place `using` statements at top of file
- Group system namespaces first, then third-party, then project
- Remove unused usings
- Use explicit namespaces, avoid `using static`

### Types & Nullability
- Enable nullable reference types (`<Nullable>enable</Nullable>`)
- Use `?` for nullable types (e.g., `MediaSession?`)
- Use `??` and `?.` operators for null-safe access
- Prefer `is` pattern matching over `== null`

### Error Handling
- Use try-catch blocks for expected exceptions
- Log errors using Serilog/ILogger
- Never swallow exceptions silently
- Use `ArgumentException` for invalid parameters
- Prefer `try-catch` over `try-catch(Exception ex)` when possible

### Event Handling
- Use `sender, args` pattern for event handlers
- Keep event handlers short; delegate to methods
- Use `?.` for thread-safe event invocation
- Unsubscribe from events when disposing

### Thread Safety
- Use `lock` for critical sections
- Prefer `Concurrent` collections for shared data
- Use `readonly` for lock objects
- Consider `async/await` for I/O operations

### Logging
- Use Serilog with structured logging
- Include timestamps in format `[HH:mm:ss.fff]`
- Use appropriate log levels (Verbose, Debug, Information, Warning, Error, Fatal)
- Include context in log messages

### IDisposable Pattern
- Implement `IDisposable` for unmanaged resources
- Call `Dispose()` when done
- Use `using` statements for automatic disposal

### Windows-Specific Guidelines
- Use Windows runtime APIs sparingly
- Handle COM exceptions gracefully
- Consider Windows version compatibility (Windows 10.0.17763.0+)

## Architecture

### Event-Driven Pattern
The application uses an event-driven architecture:
- `MediaManager` orchestrates session monitoring
- Event handlers respond to media changes
- Thread-safe console output with locking

### Project Structure
```
MediaWatcher/
├── Program.cs          # Main entry point and event handlers
├── MediaWatcher.csproj # Project configuration
└── cover.jpeg          # (asset file)
```

## Dependencies
- **Dubya.WindowsMediaController** (2.5.6): Windows media session API wrapper
- **Serilog**: Structured logging
- **Microsoft.Extensions.Logging**: Logging abstractions

## Testing

Currently no test projects exist. When adding tests:
```bash
# Create test project
dotnet new xunit -n MediaWatcher.Tests

# Run all tests
dotnet test

# Run specific test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run with verbosity
dotnet test --verbosity normal
```

## Common Tasks

### Adding a New Event Handler
1. Subscribe to event in `Main()`
2. Create handler method following naming: `MediaManager_On{EventName}`
3. Use `WriteLineColor` for console output
4. Keep logic minimal, log appropriately

### Adding Dependencies
```bash
dotnet add package PackageName --version X.Y.Z
```

### Running the Application
```bash
dotnet run
# Or after publish:
./MediaWatcher.exe
```

## Important Notes
- Application requires Windows (uses Windows Media APIs)
- Runs until user presses Enter (`Console.ReadLine()`)
- Uses Windows 10.0.17763.0 SDK (Windows 10 version 1809+)
- Single-file publish creates standalone executable
