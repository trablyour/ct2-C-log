using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Serilog;
using TaskManager.Models;
using TaskManager.Tracing;

namespace TaskManager.Services
{
    // хранение задач в JSON-файле
    public class TaskRepository
    {
        // логгер с контекстом: в логе будет видно, что запись пришла из TaskRepository
        private readonly ILogger _log = Log.ForContext<TaskRepository>();
        private readonly string _filePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public TaskRepository(string filePath)
        {
            _filePath = filePath;
            _log.Debug("Репозиторий создан, файл данных: {FilePath}", _filePath);
        }

        public List<TaskItem> Load()
        {
            using var trace = OperationTrace.Begin("LoadTasks");

            trace.Step($"проверка файла {_filePath}");
            if (!File.Exists(_filePath))
            {
                _log.Information("Файл задач не найден, начинаем с пустого списка");
                trace.Success("файла нет");
                return new List<TaskItem>();
            }

            trace.Step("чтение и разбор JSON");
            var tasks = ReadFile(_filePath);

            if (tasks == null)
            {
                trace.Fail("поврежденный файл");
                return new List<TaskItem>();
            }

            _log.Information("Загружено задач из файла: {Count}", tasks.Count);
            trace.Success($"загружено {tasks.Count}");
            return tasks;
        }

        public void Save(List<TaskItem> tasks)
        {
            using var trace = OperationTrace.Begin("SaveTasks");
            var timer = Stopwatch.StartNew();

            try
            {
                trace.Step("сериализация в JSON");
                string json = JsonSerializer.Serialize(tasks, JsonOptions);

                trace.Step("запись в файл");
                string? folder = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(_filePath, json);

                _log.Debug("Сохранено задач: {Count} за {ElapsedMs} мс", tasks.Count, timer.ElapsedMilliseconds);
                trace.Success($"сохранено {tasks.Count}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error(ex, "Не удалось сохранить задачи в {FilePath}", _filePath);
                trace.Fail(ex.Message);
                throw;
            }
        }

        // импорт задач из другого файла (возвращает null, если файл не прочитан)
        public List<TaskItem>? Import(string path)
        {
            using var trace = OperationTrace.Begin("ImportTasks");

            trace.Step($"проверка файла {path}");
            if (!File.Exists(path))
            {
                _log.Warning("Файл для импорта не найден: {Path}", path);
                trace.Fail("файл не найден");
                return null;
            }

            trace.Step("чтение и разбор JSON");
            var tasks = ReadFile(path);

            if (tasks == null)
            {
                trace.Fail("ошибка чтения");
                return null;
            }

            trace.Success($"прочитано {tasks.Count}");
            return tasks;
        }

        private List<TaskItem>? ReadFile(string path)
        {
            try
            {
                _log.Debug("Чтение файла {Path}", path);
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new List<TaskItem>();
            }
            catch (JsonException ex)
            {
                // Error с исключением: в лог попадет и текст ошибки, и стек вызовов
                _log.Error(ex, "Файл {Path} поврежден: не удалось разобрать JSON", path);
                return null;
            }
            catch (IOException ex)
            {
                _log.Error(ex, "Ошибка чтения файла {Path}", path);
                return null;
            }
        }
    }
}
