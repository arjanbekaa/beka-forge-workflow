namespace BekaForge.WorkflowKit.Cli;

internal sealed class ConsoleWaitIndicator : IDisposable
{
    private readonly TextWriter _output;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private Task? _pumpTask;
    private bool _completed;

    private ConsoleWaitIndicator(TextWriter output, string message)
    {
        _output = output;
        Message = string.IsNullOrWhiteSpace(message) ? "Working" : message.Trim();
    }

    public string Message { get; }

    public static ConsoleWaitIndicator Start(TextWriter output, string message)
    {
        var indicator = new ConsoleWaitIndicator(output, message);
        indicator.Begin();
        return indicator;
    }

    private void Begin()
    {
        lock (_sync)
        {
            _output.Write(Message);
            _output.Flush();
        }

        _pumpTask = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(350, _cts.Token).ConfigureAwait(false);

                    if (_cts.Token.IsCancellationRequested)
                        break;

                    lock (_sync)
                    {
                        _output.Write(".");
                        _output.Flush();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
        });
    }

    public void Complete(string suffix = " done.")
    {
        lock (_sync)
        {
            if (_completed)
                return;

            _completed = true;
        }

        _cts.Cancel();

        try
        {
            _pumpTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is TaskCanceledException or OperationCanceledException))
        {
            // Normal shutdown path.
        }

        lock (_sync)
        {
            _output.WriteLine(suffix);
            _output.Flush();
        }
    }

    public void Dispose()
    {
        Complete();
        _cts.Dispose();
    }
}
