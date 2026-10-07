using System.Diagnostics;
using System.Text;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using TaskManager.Models;
using TaskManager.Services;
using TaskManager.Tracing;

namespace TaskManager
{
    public static class Program
    {
        private const string LogTemplateConsole =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        private const string LogTemplateFile =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        private static TaskService _service = null!;
        private static TextWriterTraceListener _fileTrace = null!;
        private static readonly ColorConsoleTraceListener ConsoleTrace = new();
        private static bool _consoleTraceOn;
        private static string _traceFile = "";

        // для логов из самого Program.
        // именно свойство, а не поле: поле создалось бы до настройки Log.Logger и ничего бы не писало
        private static ILogger AppLog => Log.ForContext("SourceContext", "Program");

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // ================= инициализация Serilog =================
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                // консоль
                .WriteTo.Console(
                    outputTemplate: LogTemplateConsole,
                    theme: AnsiConsoleTheme.Code)
                // общий файл: новый файл каждый день, имя вида log-20261007.txt
                .WriteTo.File(
                    "logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: LogTemplateFile)
                // отдельный файл только для предупреждений и ошибок
                .WriteTo.File(
                    "logs/errors-.txt",
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: LogTemplateFile)
                .CreateLogger();
            // =========================================================

            try
            {
                AppLog.Information("Приложение TaskManager запущено");
                AppLog.Debug("Аргументы запуска: {Args}", args);
                AppLog.Debug("Рабочая папка: {Folder}", Directory.GetCurrentDirectory());

                SetupTracing();

                _service = new TaskService(new TaskRepository(Path.Combine("data", "tasks.json")));

                if (args.Contains("--demo"))
                    RunDemo();
                else
                    RunMenu();
            }
            catch (Exception ex)
            {
                // Fatal - самый высокий уровень: приложение не может продолжать работу
                AppLog.Fatal(ex, "Приложение аварийно завершилось");
                Console.WriteLine("Критическая ошибка: " + ex.Message);
            }
            finally
            {
                AppLog.Information("Приложение TaskManager завершено");

                Trace.WriteLine($"========== Завершение {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
                Trace.Flush();
                _fileTrace?.Dispose();

                // обязательно: дописывает буфер логов в файлы перед выходом
                Log.CloseAndFlush();
            }
        }

        // трассировка из КТ1: в файл всегда, в консоль по желанию (по умолчанию выключена)
        private static void SetupTracing()
        {
            Directory.CreateDirectory("logs");
            _traceFile = Path.Combine("logs", $"trace-{DateTime.Now:yyyyMMdd}.txt");
            _fileTrace = new TextWriterTraceListener(_traceFile, "FileTrace");

            Trace.AutoFlush = true;
            Trace.IndentSize = 4;
            Trace.Listeners.Add(_fileTrace);

            Trace.WriteLine("");
            Trace.WriteLine($"========== Запуск {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");

            AppLog.Debug("Трассировка пишется в {TraceFile}", _traceFile);
        }

        // ================= меню =================

        private static void RunMenu()
        {
            while (true)
            {
                PrintMenu();
                Console.Write("Выберите пункт: ");
                string? choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                switch (choice)
                {
                    case "1": AddTask(); break;
                    case "2": PrintTasks(_service.GetAll()); break;
                    case "3": DeleteTask(); break;
                    case "4": CompleteTask(); break;
                    case "5": ImportTasks(); break;
                    case "6": ToggleConsoleTrace(); break;
                    case "7": ShowLogFiles(); break;
                    case "0": return;
                    default:
                        AppLog.Warning("Неизвестный пункт меню: {Choice}", choice);
                        Error("Нет такого пункта меню");
                        break;
                }

                Console.WriteLine();
            }
        }

        private static void PrintMenu()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========== TaskManager (Serilog) ===========");
            Console.ResetColor();
            Console.WriteLine(" 1. Добавить задачу");
            Console.WriteLine(" 2. Показать задачи");
            Console.WriteLine(" 3. Удалить задачу");
            Console.WriteLine(" 4. Отметить задачу выполненной");
            Console.WriteLine(" 5. Импорт задач из JSON-файла");
            Console.WriteLine($" 6. Трассировка в консоли: {(_consoleTraceOn ? "ВКЛ" : "ВЫКЛ")}");
            Console.WriteLine(" 7. Показать файлы логов");
            Console.WriteLine(" 0. Выход");
        }

        private static void AddTask()
        {
            Console.Write("Название задачи: ");
            string? title = Console.ReadLine();

            Console.Write("Приоритет (1 - низкий, 2 - средний, 3 - высокий) [2]: ");
            string? input = Console.ReadLine()?.Trim();

            var priority = TaskPriority.Medium;
            if (!string.IsNullOrEmpty(input))
            {
                if (int.TryParse(input, out int p) && Enum.IsDefined(typeof(TaskPriority), p))
                    priority = (TaskPriority)p;
                else
                    AppLog.Warning("Некорректный приоритет {Input}, использован средний", input);
            }

            ShowResult(_service.Add(title, priority));
        }

        private static void DeleteTask()
        {
            int? id = ReadId("Id задачи для удаления: ");
            if (id != null)
                ShowResult(_service.Delete(id.Value));
        }

        private static void CompleteTask()
        {
            int? id = ReadId("Id выполненной задачи: ");
            if (id != null)
                ShowResult(_service.Complete(id.Value));
        }

        private static void ImportTasks()
        {
            Console.Write("Путь к JSON-файлу: ");
            string path = Console.ReadLine()?.Trim().Trim('"') ?? "";
            ShowResult(_service.Import(path));
        }

        private static int? ReadId(string prompt)
        {
            Console.Write(prompt);
            string? input = Console.ReadLine()?.Trim();

            if (int.TryParse(input, out int id) && id > 0)
                return id;

            AppLog.Warning("Введен некорректный Id: {Input}", input);
            Error("Id должен быть положительным числом");
            return null;
        }

        private static void ToggleConsoleTrace()
        {
            _consoleTraceOn = !_consoleTraceOn;

            if (_consoleTraceOn)
                Trace.Listeners.Add(ConsoleTrace);
            else
                Trace.Listeners.Remove(ConsoleTrace);

            AppLog.Information("Трассировка в консоли {State}", _consoleTraceOn ? "включена" : "выключена");
        }

        private static void ShowLogFiles()
        {
            if (!Directory.Exists("logs"))
            {
                Info("Папка logs пока пуста");
                return;
            }

            Info($"Папка: {Path.GetFullPath("logs")}");
            foreach (var file in new DirectoryInfo("logs").GetFiles().OrderBy(f => f.Name))
                Info($"  {file.Name,-26} {file.Length,8} байт   {file.LastWriteTime:dd.MM.yyyy HH:mm:ss}");

            AppLog.Debug("Пользователь просмотрел список файлов логов");
        }

        // ================= демонстрация =================

        private static void RunDemo()
        {
            AppLog.Information("Запущен демонстрационный сценарий");

            DemoStep(1, "Добавляем три задачи (Information)");
            _service.Add("Подготовить отчет по Serilog", TaskPriority.High);
            _service.Add("Купить продукты", TaskPriority.Low);
            _service.Add("Сделать домашнее задание", TaskPriority.Medium);

            DemoStep(2, "Пустое название задачи (Warning)");
            ShowResult(_service.Add("   "));

            DemoStep(3, "Список задач");
            var tasks = _service.GetAll();
            PrintTasks(tasks);

            DemoStep(4, "Выполняем задачу и пробуем выполнить ее повторно (Warning)");
            if (tasks.Count > 0)
            {
                ShowResult(_service.Complete(tasks[0].Id));
                ShowResult(_service.Complete(tasks[0].Id));
            }

            DemoStep(5, "Удаляем несуществующую задачу #999 (Warning)");
            ShowResult(_service.Delete(999));

            DemoStep(6, "Импорт из поврежденного файла (Error)");
            Directory.CreateDirectory("data");
            string broken = Path.Combine("data", "broken.json");
            File.WriteAllText(broken, "{ это не JSON ]");
            ShowResult(_service.Import(broken));

            DemoStep(7, "Итоговый список");
            PrintTasks(_service.GetAll());

            Console.WriteLine();
            ShowLogFiles();
        }

        private static void DemoStep(int number, string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"--- Шаг {number}. {title} ---");
            Console.ResetColor();
        }

        // ================= вывод =================

        private static void PrintTasks(IReadOnlyList<TaskItem> list)
        {
            if (list.Count == 0)
            {
                Info("Список задач пуст");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($" {"Id",-4} {"Статус",-8} {"Приоритет",-10} {"Создана",-17} Название");
            Console.WriteLine(" " + new string('-', 75));

            foreach (var t in list)
            {
                Console.ForegroundColor = t.IsDone ? ConsoleColor.DarkGray
                    : t.Priority == TaskPriority.High ? ConsoleColor.Yellow
                    : ConsoleColor.White;

                string status = t.IsDone ? "готово" : "в работе";
                string priority = t.Priority switch
                {
                    TaskPriority.High => "высокий",
                    TaskPriority.Low => "низкий",
                    _ => "средний"
                };

                Console.WriteLine($" {t.Id,-4} {status,-8} {priority,-10} {t.CreatedAt,-17:dd.MM.yyyy HH:mm} {t.Title}");
            }

            Console.ResetColor();
            Console.WriteLine($" Всего: {list.Count}, выполнено: {list.Count(t => t.IsDone)}");
        }

        private static void ShowResult(OperationResult result)
        {
            if (result.Success)
                WriteColored("✓ " + result.Message, ConsoleColor.Green);
            else
                Error(result.Message);
        }

        private static void Error(string text) => WriteColored("✗ " + text, ConsoleColor.Red);

        private static void Info(string text) => WriteColored(text, ConsoleColor.Gray);

        private static void WriteColored(string text, ConsoleColor color)
        {
            lock (ColorConsoleTraceListener.ConsoleLock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
                Console.ResetColor();
            }
        }
    }
}
