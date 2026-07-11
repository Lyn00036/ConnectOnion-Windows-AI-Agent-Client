using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConnectOnion.Protocol.Runtime;

/// <summary>
/// App-level, thread-safe owner of every in-flight agent turn. This is the single
/// source of truth for "what is streaming right now"; pages and view models only
/// subscribe to it. A run keeps executing (and persists its reply) after the page that
/// started it is gone, so switching sessions, minimizing to the tray, or reopening the
/// conversation never interrupts or loses a reply.
///
/// Design invariants:
/// <list type="bullet">
/// <item>At most one active run per conversation; different conversations run in parallel.</item>
/// <item>Every state transition is published as an immutable snapshot with a monotonic sequence.</item>
/// <item>A terminal state (Completed/Failed/Cancelled) is set exactly once.</item>
/// <item>On success the reply is persisted <b>before</b> the run is marked Completed.</item>
/// <item>No network/DB/UI work happens while holding a run's lock.</item>
/// </list>
/// </summary>
public sealed class ConversationRunRegistry
{
    private readonly IRunPersistence _persistence;
    private readonly IRunLogger _logger;
    private readonly Action<Exception>? _onError;

    private readonly ConcurrentDictionary<string, ConversationRun> _active = new();
    private readonly ConcurrentDictionary<string, ConversationRunSnapshot> _lastSnapshots = new();
    private readonly ConcurrentDictionary<string, TurnRequest> _retryable = new();
    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

    private volatile bool _shuttingDown;

    public ConversationRunRegistry(
        IRunPersistence persistence,
        IRunLogger? logger = null,
        Action<Exception>? onError = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? NullRunLogger.Instance;
        _onError = onError;
    }

    /// <summary>
    /// Registers and starts a run for <paramref name="request"/>. Throws
    /// <see cref="InvalidOperationException"/> if the conversation already has an active
    /// run (the caller should gate its send UI on <see cref="GetActiveRun"/>).
    /// </summary>
    public ConversationRunSnapshot StartRun(TurnRequest request, ITurnExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        if (_shuttingDown) throw new InvalidOperationException("The registry is shutting down.");

        var run = new ConversationRun
        {
            Request = request,
            Cts = new CancellationTokenSource(),
        };

        if (!_active.TryAdd(request.ConversationId, run))
        {
            run.Cts.Dispose();
            throw new InvalidOperationException(
                $"Conversation '{request.ConversationId}' already has an active run.");
        }

        _retryable.TryRemove(request.ConversationId, out _);

        ConversationRunSnapshot snapshot;
        lock (run.Gate)
        {
            run.Sequence++;
            snapshot = run.SnapshotLocked();
        }
        StoreAndPublish(snapshot, "Queued", run.CreatedAt);

        run.ExecutionTask = Task.Run(() => RunAsync(run, executor));
        return snapshot;
    }

    /// <summary>The active (non-terminal) run for a conversation, or the last terminal
    /// snapshot if one recently finished, or null. Used by a page opening a conversation
    /// to decide whether to resume live streaming.</summary>
    public ConversationRunSnapshot? GetActiveRun(string conversationId)
    {
        if (_active.TryGetValue(conversationId, out var run))
        {
            lock (run.Gate) return run.SnapshotLocked();
        }
        return _lastSnapshots.TryGetValue(conversationId, out var last) ? last : null;
    }

    /// <summary>Snapshots of every currently-executing run across all conversations.</summary>
    public IReadOnlyList<ConversationRunSnapshot> GetActiveRuns()
    {
        var list = new List<ConversationRunSnapshot>();
        foreach (var run in _active.Values)
        {
            lock (run.Gate) list.Add(run.SnapshotLocked());
        }
        return list;
    }

    /// <summary>
    /// Subscribes to run updates for one conversation. The handler is invoked immediately
    /// with the current snapshot (if any) so a page created mid-turn resumes instantly,
    /// then on every subsequent change. Dispose the result to detach — this never cancels
    /// the underlying run, so closing a page/window leaves the turn running.
    /// </summary>
    public IDisposable Subscribe(string conversationId, Action<ConversationRunSnapshot> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = _subscriptions.GetOrAdd(conversationId, _ => new Subscription());
        var token = subscription.Add(handler);

        var current = GetActiveRun(conversationId);
        if (current is not null) SafeInvoke(handler, current);

        return new Unsubscriber(subscription, token);
    }

    /// <summary>Cancels the active run if its id matches, and waits (bounded) for it to
    /// unwind to a terminal state. A no-op if the run already finished.</summary>
    public async Task CancelRunAsync(string conversationId, string runId)
    {
        if (!_active.TryGetValue(conversationId, out var run) || run.RunId != runId) return;

        run.Cts.Cancel();
        var task = run.ExecutionTask;
        if (task is not null)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            catch { /* bounded wait; RunAsync's own finalize is authoritative */ }
        }
    }

    /// <summary>
    /// Starts a fresh run for a conversation whose last run failed, reusing the original
    /// user message but minting a new RunId and AssistantMessageId. Returns null if there
    /// is nothing retryable (e.g. the last run succeeded, or a run is already active).
    /// </summary>
    public ConversationRunSnapshot? RetryRun(string conversationId, ITurnExecutor executor)
    {
        if (!_retryable.TryGetValue(conversationId, out var original)) return null;
        if (_active.ContainsKey(conversationId)) return null;

        var retry = original with
        {
            RunId = Guid.NewGuid().ToString(),
            AssistantMessageId = Guid.NewGuid().ToString(),
        };
        return StartRun(retry, executor);
    }

    /// <summary>
    /// Real app shutdown: stop accepting new runs, cancel everything in flight, and wait a
    /// bounded time for each to persist recoverable state and release its transport. Never
    /// blocks forever. Safe to call once.
    /// </summary>
    public async Task ShutdownAsync(TimeSpan timeout)
    {
        _shuttingDown = true;
        var runs = _active.Values.ToArray();
        foreach (var run in runs) run.Cts.Cancel();

        var tasks = runs.Select(r => r.ExecutionTask).Where(t => t is not null).Cast<Task>().ToArray();
        if (tasks.Length == 0) return;

        try { await Task.WhenAll(tasks).WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            var stragglers = _active.Values.Select(r => r.RunId);
            _onError?.Invoke(new TimeoutException(
                $"Shutdown timed out with runs still finalizing: {string.Join(", ", stragglers)}"));
        }
        catch (Exception ex) { _onError?.Invoke(ex); }
    }

    // ----- the background turn -----

    private async Task RunAsync(ConversationRun run, ITurnExecutor executor)
    {
        var sink = new Sink(this, run);
        try
        {
            run.StartedAt = DateTimeOffset.UtcNow;
            var finalReply = await executor.ExecuteAsync(run.Request, sink).ConfigureAwait(false);

            // Persist BEFORE flipping to Completed: a subscriber that reacts to Completed
            // by reloading the DB must find the reply already committed.
            var pending = SnapshotFor(run, ConversationRunStatus.Completed);
            await _persistence.PersistCompletedAsync(pending, finalReply).ConfigureAwait(false);

            Finalize(run, ConversationRunStatus.Completed, finalReply, null, null);
        }
        catch (OperationCanceledException) when (run.Cts.IsCancellationRequested)
        {
            await FinalizeFailureAsync(run, ConversationRunStatus.Cancelled, "cancelled", "The turn was cancelled.");
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(run, ConversationRunStatus.Failed, ex.GetType().Name, ex.Message);
        }
        finally
        {
            _active.TryRemove(new KeyValuePair<string, ConversationRun>(run.ConversationId, run));
            run.Cts.Dispose();
        }
    }

    private async Task FinalizeFailureAsync(
        ConversationRun run, ConversationRunStatus status, string code, string message)
    {
        // Persist the partial/error snapshot best-effort so a crash-then-reopen still shows
        // what was generated; never let a persistence failure mask the terminal transition.
        try
        {
            var pending = SnapshotFor(run, status, code, message);
            await _persistence.PersistFailedAsync(pending).ConfigureAwait(false);
        }
        catch (Exception ex) { _onError?.Invoke(ex); }

        Finalize(run, status, null, code, message);
        if (status == ConversationRunStatus.Failed) _retryable[run.ConversationId] = run.Request;
    }

    private ConversationRunSnapshot SnapshotFor(
        ConversationRun run, ConversationRunStatus status, string? code = null, string? message = null)
    {
        lock (run.Gate)
        {
            if (code is not null) run.ErrorCode = code;
            if (message is not null) run.ErrorMessage = message;
            return run.SnapshotLocked(status);
        }
    }

    private void Finalize(
        ConversationRun run, ConversationRunStatus status,
        string? finalReply, string? code, string? message)
    {
        ConversationRunSnapshot snapshot;
        lock (run.Gate)
        {
            if (run.Finalized) return; // terminal exactly once
            run.Finalized = true;
            run.Status = status;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = code;
            run.ErrorMessage = message;
            if (finalReply is not null && finalReply.Length > 0) run.PartialContent = finalReply;
            run.Sequence++;
            snapshot = run.SnapshotLocked();
        }
        var elapsed = (run.CompletedAt ?? DateTimeOffset.UtcNow) - run.CreatedAt;
        StoreAndPublish(snapshot, status.ToString(), run.CreatedAt, elapsed);
    }

    // ----- sink callbacks (from the executor thread) -----

    private void SetStatus(ConversationRun run, ConversationRunStatus status)
    {
        ConversationRunSnapshot snapshot;
        lock (run.Gate)
        {
            if (run.Finalized || run.Status == status) return;
            run.Status = status;
            run.Sequence++;
            snapshot = run.SnapshotLocked();
        }
        StoreAndPublish(snapshot, status.ToString(), run.CreatedAt);
    }

    private void PublishEvent(ConversationRun run, AgentStreamEvent streamEvent)
    {
        ConversationRunSnapshot snapshot;
        lock (run.Gate)
        {
            if (run.Finalized) return;
            run.Events.Add(streamEvent);
            run.Sequence++;
            snapshot = run.SnapshotLocked();
        }
        StoreAndPublish(snapshot, "Streaming", run.CreatedAt);
    }

    private void UpdatePartial(ConversationRun run, string partial)
    {
        ConversationRunSnapshot snapshot;
        lock (run.Gate)
        {
            if (run.Finalized || run.PartialContent == partial) return;
            run.PartialContent = partial;
            run.Sequence++;
            snapshot = run.SnapshotLocked();
        }
        StoreAndPublish(snapshot, "Streaming", run.CreatedAt);
    }

    // ----- publish / subscriber fan-out -----

    private void StoreAndPublish(
        ConversationRunSnapshot snapshot, string phase, DateTimeOffset createdAt, TimeSpan? elapsed = null)
    {
        _lastSnapshots[snapshot.ConversationId] = snapshot;

        try { _logger.Log(snapshot, phase, elapsed ?? (DateTimeOffset.UtcNow - createdAt)); }
        catch (Exception ex) { _onError?.Invoke(ex); }

        if (_subscriptions.TryGetValue(snapshot.ConversationId, out var subscription))
        {
            foreach (var handler in subscription.Handlers()) SafeInvoke(handler, snapshot);
        }
    }

    private void SafeInvoke(Action<ConversationRunSnapshot> handler, ConversationRunSnapshot snapshot)
    {
        // One subscriber throwing must not starve the others or break the run.
        try { handler(snapshot); }
        catch (Exception ex) { _onError?.Invoke(ex); }
    }

    private sealed class Sink : IRunSink
    {
        private readonly ConversationRunRegistry _registry;
        private readonly ConversationRun _run;
        public Sink(ConversationRunRegistry registry, ConversationRun run) { _registry = registry; _run = run; }

        public CancellationToken CancellationToken => _run.Cts.Token;
        public void SetConnecting() => _registry.SetStatus(_run, ConversationRunStatus.Connecting);
        public void SetRunning() => _registry.SetStatus(_run, ConversationRunStatus.Running);
        public void Publish(AgentStreamEvent streamEvent) => _registry.PublishEvent(_run, streamEvent);
        public void UpdatePartial(string partialContent) => _registry.UpdatePartial(_run, partialContent);
    }

    // Copy-on-write handler set so fan-out never holds a lock while invoking UI callbacks.
    private sealed class Subscription
    {
        private readonly object _gate = new();
        private ImmutableEntry[] _entries = Array.Empty<ImmutableEntry>();

        public long Add(Action<ConversationRunSnapshot> handler)
        {
            lock (_gate)
            {
                var token = _next++;
                _entries = _entries.Append(new ImmutableEntry(token, handler)).ToArray();
                return token;
            }
        }

        public void Remove(long token)
        {
            lock (_gate)
            {
                _entries = _entries.Where(e => e.Token != token).ToArray();
            }
        }

        public IEnumerable<Action<ConversationRunSnapshot>> Handlers()
        {
            var snapshot = _entries; // volatile-enough: reference read of an immutable array
            foreach (var e in snapshot) yield return e.Handler;
        }

        private long _next;
        private readonly record struct ImmutableEntry(long Token, Action<ConversationRunSnapshot> Handler);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private Subscription? _subscription;
        private readonly long _token;
        public Unsubscriber(Subscription subscription, long token) { _subscription = subscription; _token = token; }
        public void Dispose()
        {
            var s = Interlocked.Exchange(ref _subscription, null);
            s?.Remove(_token);
        }
    }
}
