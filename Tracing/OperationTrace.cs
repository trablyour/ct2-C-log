using System.Diagnostics;

namespace TaskManager.Tracing
{
    // простая трассировка операции: старт, шаги, результат и время выполнения
    //
    // using var trace = OperationTrace.Begin("AddTask");
    // trace.Step("проверка данных");
    // trace.Success("создана задача #5");
    public sealed class OperationTrace : IDisposable
    {
        private static int _counter;

        private readonly int _id;
        private readonly string _operation;
        private readonly Stopwatch _timer;
        private int _step;
        private bool _finished;

        private OperationTrace(string operation)
        {
            _id = Interlocked.Increment(ref _counter);
            _operation = operation;
            _timer = Stopwatch.StartNew();

            Write($">> START {_operation}");
            Trace.Indent();
        }

        public static OperationTrace Begin(string operation) => new OperationTrace(operation);

        public void Step(string description)
        {
            _step++;
            Write($"шаг {_step}: {description}");
        }

        public void Success(string? result = null)
        {
            Finish("OK", result);
        }

        public void Fail(string reason)
        {
            Finish("FAIL", reason);
        }

        public void Dispose()
        {
            // если забыли вызвать Success/Fail (например, вылетело исключение)
            if (!_finished)
                Finish("ABORTED", "операция прервана исключением");
        }

        private void Finish(string status, string? details)
        {
            if (_finished)
                return;

            _finished = true;
            _timer.Stop();
            Trace.Unindent();

            string text = $"<< END {_operation}: {status} за {_timer.Elapsed.TotalMilliseconds:0.00} мс";
            if (!string.IsNullOrEmpty(details))
                text += $" ({details})";

            Write(text);
        }

        private void Write(string message)
        {
            Trace.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [op#{_id}] {message}");
        }
    }
}
