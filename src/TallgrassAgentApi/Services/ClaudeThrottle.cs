using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class ClaudeThrottle
{
    private readonly SemaphoreSlim          _semaphore;
    private readonly int                    _maxConcurrent;
    private readonly int                    _nodeParallelism;
    private readonly int                    _maxWaitMs;

    private int _active    = 0;
    private int _waiting   = 0;
    private int _completed = 0;
    private int _rejected  = 0;

    public ClaudeThrottle(IConfiguration config)
    {
        _maxConcurrent = config.GetValue<int>("ClaudeThrottle:MaxConcurrent", 3);
        _nodeParallelism = config.GetValue<int>("ClaudeThrottle:NodeParallelism", _maxConcurrent);
        _maxWaitMs     = config.GetValue<int>("ClaudeThrottle:MaxWaitMs",     8000);
       
        if (_maxConcurrent <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration value 'ClaudeThrottle:MaxConcurrent' must be greater than 0, but was {_maxConcurrent}.");
        }
        if (_nodeParallelism <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration value 'ClaudeThrottle:NodeParallelism' must be greater than 0, but was {_nodeParallelism}.");
        }
        if (_maxWaitMs < -1)
        {
            throw new InvalidOperationException(
                $"Configuration value 'ClaudeThrottle:MaxWaitMs' must be greater than or equal to -1, but was {_maxWaitMs}.");
        }
        _semaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
    }

    /// <summary>
    /// Acquires a slot. Returns a disposable that releases on dispose.
    /// Throws ThrottleRejectedException if the wait timeout is exceeded.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _waiting);
        try
        {
            bool acquired = await _semaphore.WaitAsync(_maxWaitMs, cancellationToken);
            if (!acquired)
            {
                Interlocked.Increment(ref _rejected);
                throw new ThrottleRejectedException(
                    $"Claude API concurrency limit ({_maxConcurrent}) reached. " +
                    $"Request waited {_maxWaitMs}ms and was rejected.");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        Interlocked.Increment(ref _active);
        return new Releaser(this);
    }

    internal void Release()
    {
        Interlocked.Decrement(ref _active);
        Interlocked.Increment(ref _completed);
        _semaphore.Release();
    }

    public QueueSnapshot Snapshot() => new()
    {
        MaxConcurrent  = _maxConcurrent,
        NodeParallelism = _nodeParallelism,
        ActiveCalls    = _active,
        WaitingCalls   = _waiting,
        CompletedCalls = _completed,
        RejectedCalls  = _rejected
    };

    private sealed class Releaser : IDisposable
    {
        private readonly ClaudeThrottle _owner;
        private bool _disposed;

        internal Releaser(ClaudeThrottle owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.Release();
        }
    }
}

public class ThrottleRejectedException : Exception
{
    public ThrottleRejectedException(string message) : base(message) { }
}