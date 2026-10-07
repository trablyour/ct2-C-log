using Serilog;
using TaskManager.Models;
using TaskManager.Tracing;

namespace TaskManager.Services
{
    public class OperationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";

        public static OperationResult Ok(string message) => new() { Success = true, Message = message };
        public static OperationResult Error(string message) => new() { Success = false, Message = message };
    }

    public class TaskService
    {
        public const int MaxTitleLength = 100;

        private readonly ILogger _log = Log.ForContext<TaskService>();
        private readonly TaskRepository _repository;
        private readonly List<TaskItem> _tasks;

        public TaskService(TaskRepository repository)
        {
            _repository = repository;
            _tasks = _repository.Load();
            _log.Debug("Сервис задач инициализирован, задач в памяти: {Count}", _tasks.Count);
        }

        // ---------- создание ----------

        public OperationResult Add(string? title, TaskPriority priority = TaskPriority.Medium)
        {
            using var trace = OperationTrace.Begin("AddTask");
            _log.Debug("Добавление задачи: название \"{Title}\", приоритет {Priority}", title, priority);

            trace.Step("проверка названия");
            title = title?.Trim() ?? "";

            if (title.Length == 0)
            {
                _log.Warning("Попытка создать задачу с пустым названием");
                trace.Fail("пустое название");
                return OperationResult.Error("Название задачи не может быть пустым");
            }

            if (title.Length > MaxTitleLength)
            {
                _log.Warning("Слишком длинное название задачи: {Length} символов (максимум {Max})", title.Length, MaxTitleLength);
                trace.Fail("слишком длинное название");
                return OperationResult.Error($"Название не должно быть длиннее {MaxTitleLength} символов");
            }

            trace.Step("вычисление нового Id");
            int id = _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;
            _log.Debug("Новый Id задачи: {Id}", id);

            trace.Step("создание объекта задачи");
            var task = new TaskItem
            {
                Id = id,
                Title = title,
                Priority = priority,
                CreatedAt = DateTime.Now
            };
            _tasks.Add(task);

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            // структурное логирование: Id, Title и Priority сохраняются как отдельные свойства
            _log.Information("Создана задача #{Id} \"{Title}\" с приоритетом {Priority}", task.Id, task.Title, task.Priority);

            trace.Success($"задача #{task.Id}");
            return OperationResult.Ok($"Задача #{task.Id} добавлена");
        }

        // ---------- удаление ----------

        public OperationResult Delete(int id)
        {
            using var trace = OperationTrace.Begin("DeleteTask");
            _log.Debug("Запрос на удаление задачи #{Id}", id);

            trace.Step($"поиск задачи #{id}");
            var task = _tasks.FirstOrDefault(t => t.Id == id);

            if (task == null)
            {
                _log.Warning("Удаление: задача #{Id} не найдена", id);
                trace.Fail("задача не найдена");
                return OperationResult.Error($"Задача #{id} не найдена");
            }

            trace.Step("удаление из списка");
            _tasks.Remove(task);

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            _log.Information("Удалена задача #{Id} \"{Title}\"", task.Id, task.Title);

            trace.Success($"удалена #{id}");
            return OperationResult.Ok($"Задача #{id} \"{task.Title}\" удалена");
        }

        // ---------- выполнение ----------

        public OperationResult Complete(int id)
        {
            using var trace = OperationTrace.Begin("CompleteTask");
            _log.Debug("Запрос на выполнение задачи #{Id}", id);

            trace.Step($"поиск задачи #{id}");
            var task = _tasks.FirstOrDefault(t => t.Id == id);

            if (task == null)
            {
                _log.Warning("Выполнение: задача #{Id} не найдена", id);
                trace.Fail("задача не найдена");
                return OperationResult.Error($"Задача #{id} не найдена");
            }

            if (task.IsDone)
            {
                _log.Warning("Задача #{Id} уже была выполнена {CompletedAt}", id, task.CompletedAt);
                trace.Fail("уже выполнена");
                return OperationResult.Error($"Задача #{id} уже выполнена");
            }

            trace.Step("отметка выполнения");
            task.IsDone = true;
            task.CompletedAt = DateTime.Now;

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            _log.Information("Задача #{Id} \"{Title}\" выполнена", task.Id, task.Title);

            trace.Success($"выполнена #{id}");
            return OperationResult.Ok($"Задача #{id} отмечена выполненной");
        }

        // ---------- импорт ----------

        public OperationResult Import(string path)
        {
            using var trace = OperationTrace.Begin("Import");
            _log.Debug("Запрос на импорт задач из {Path}", path);

            trace.Step("чтение файла");
            var imported = _repository.Import(path);

            if (imported == null)
            {
                trace.Fail("файл не прочитан");
                return OperationResult.Error("Не удалось импортировать задачи, подробности в логе");
            }

            trace.Step("добавление задач с новыми Id");
            int nextId = _tasks.Count == 0 ? 1 : _tasks.Max(t => t.Id) + 1;
            foreach (var task in imported.Where(t => !string.IsNullOrWhiteSpace(t.Title)))
            {
                task.Id = nextId++;
                _tasks.Add(task);
            }

            trace.Step("сохранение в хранилище");
            _repository.Save(_tasks);

            _log.Information("Импортировано задач: {Count} из {Path}", imported.Count, path);
            trace.Success($"импортировано {imported.Count}");
            return OperationResult.Ok($"Импортировано задач: {imported.Count}");
        }

        // ---------- вывод списка ----------

        public IReadOnlyList<TaskItem> GetAll()
        {
            using var trace = OperationTrace.Begin("ListTasks");

            trace.Step("сортировка: сначала невыполненные, затем по приоритету");
            var result = _tasks
                .OrderBy(t => t.IsDone)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.Id)
                .ToList();

            int done = result.Count(t => t.IsDone);
            _log.Information("Запрошен список задач: всего {Total}, выполнено {Done}", result.Count, done);

            if (result.Count(t => !t.IsDone && t.Priority == TaskPriority.High) >= 3)
                _log.Warning("Много невыполненных задач с высоким приоритетом");

            trace.Success($"{result.Count} задач");
            return result;
        }
    }
}
