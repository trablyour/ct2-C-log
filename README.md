# TaskManager + Serilog

Консольное приложение для управления задачами. В КТ2 встроенное логирование заменено на **Serilog**.

## Пакеты

```bash
dotnet add package Serilog
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

## Инициализация логгера (Program.Main)

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: LogTemplateConsole, theme: AnsiConsoleTheme.Code)
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .WriteTo.File("logs/errors-.txt", restrictedToMinimumLevel: LogEventLevel.Warning,
                  rollingInterval: RollingInterval.Day)
    .CreateLogger();
```

`RollingInterval.Day` добавляет дату к имени файла: `logs/log-20261007.txt`.
В конце работы вызывается `Log.CloseAndFlush()`, чтобы все записи дописались в файл.

## Где какие уровни

| Уровень | Где | Пример |
|---|---|---|
| Debug | `TaskService`, `TaskRepository`, `Program` | новый Id задачи, время сохранения файла, рабочая папка |
| Information | `TaskService`, `Program` | задача создана / удалена / выполнена, запуск и завершение |
| Warning | `TaskService`, `Program` | пустое название, задача не найдена, неверный пункт меню |
| Error | `TaskRepository` | поврежденный JSON при импорте, ошибка записи файла |
| Fatal | `Program.Main` | необработанное исключение |

## Файлы логов

| Файл | Содержимое |
|---|---|
| `logs/log-ГГГГММДД.txt` | все записи начиная с Debug |
| `logs/errors-ГГГГММДД.txt` | только Warning, Error, Fatal |
| `logs/trace-ГГГГММДД.txt` | пошаговая трассировка операций (из КТ1) |

## Запуск

```bash
dotnet run              # меню
dotnet run -- --demo    # сценарий, где срабатывают все уровни логов
```
