using PrintFlow.Design;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.DesignCloud;

internal sealed class DesignJobs : IDisposable
{
    private readonly DesignReviewPipeline _pipeline;
    private readonly IHostApplicationLifetime _application;
    private readonly ILogger<DesignJobs> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _jobs = [];
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    private readonly Timer _expiration;
    private int _running;

    public DesignJobs(DesignReviewPipeline pipeline, IHostApplicationLifetime application, ILogger<DesignJobs> logger)
    {
        _pipeline = pipeline;
        _application = application;
        _logger = logger;
        _expiration = new Timer(_ =>
        {
            lock (_gate) RemoveExpired();
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Dispose() => _expiration.Dispose();

    public bool Start(Guid id, CloudDesignInput input)
    {
        lock (_gate)
        {
            RemoveExpired();
            if (_jobs.ContainsKey(id)) return true;
            if (_jobs.Count >= 8 || _running >= 2) return false;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_application.ApplicationStopping);
            var entry = new Entry(cancellation);
            _jobs.Add(id, entry);
            _running++;
            _ = Task.Run(() => ExecuteAsync(id, entry, input));
            return true;
        }
    }

    public CloudDesignJob? Find(Guid id)
    {
        lock (_gate)
        {
            RemoveExpired();
            return _jobs.GetValueOrDefault(id)?.Status;
        }
    }

    public void CancelOrForget(Guid id)
    {
        lock (_gate)
        {
            if (!_jobs.Remove(id, out var entry)) return;
            if (entry.CompletedAt is null) entry.Cancellation.Cancel();
        }
    }

    private async Task ExecuteAsync(Guid id, Entry entry, CloudDesignInput input)
    {
        CloudDesignJob result;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(entry.Cancellation.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(CloudDesignLimits.CloudTimeoutMinutes));
        try
        {
            result = await _pipeline.RunAsync(input, deadline.Token, stage =>
            {
                lock (_gate) entry.Status = new(stage, []);
            }, entry.Cancellation.Token);
            foreach (var attempt in result.Attempts)
                _logger.LogInformation("Design job {JobId} generation {GenerationResponseId}, review {ReviewResponseId}, composite review {CompositeReviewResponseId}.",
                    id, attempt.Generation.ResponseId, attempt.Review?.ResponseId, attempt.CompositeReview?.ResponseId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            result = new(CloudDesignStage.Cancelled, []);
        }
        catch (Exception error)
        {
            // Upstream exception messages can contain the user's input or server details.
            _logger.LogWarning("Design job {JobId} failed with {FailureType}.", id, error.GetType().Name);
            result = new(CloudDesignStage.Unavailable, []);
        }

        lock (_gate)
        {
            entry.Status = result;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            entry.Cancellation.Dispose();
            _running--;
        }
    }

    private void RemoveExpired()
    {
        var threshold = DateTimeOffset.UtcNow - Retention;
        var expired = _jobs.Where(pair => pair.Value.CompletedAt < threshold).Select(pair => pair.Key).ToArray();
        foreach (Guid id in expired) _jobs.Remove(id);
    }

    private sealed class Entry(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CloudDesignJob Status { get; set; } = new(CloudDesignStage.Generating, []);
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
