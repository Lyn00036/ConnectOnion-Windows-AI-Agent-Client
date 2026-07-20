using System;
using System.Collections.Generic;
using System.Threading;

namespace ConnectOnion.WinUIClient.Services.Speech;

/// <summary>
/// Accumulates what a dictation session heard, and produces the text to insert <b>once</b>, when
/// the user stops recording.
///
/// <para>A single write path is the deliberate design. The recogniser emits two kinds of text:
/// <i>hypotheses</i>, which are running guesses revised repeatedly as the user keeps talking, and
/// <i>results</i>, which are finalised utterances. Writing either one into the composer while
/// recording means the box changes under the user — guesses appear and are rewritten, and
/// anything they type mid-session has to be reconciled against text the recogniser is still
/// revising. Collecting everything here and inserting it on stop removes that whole class of
/// problem: during recording the composer is untouched, so the user can type freely, and the
/// insertion happens once against whatever the box contains at that moment.</para>
///
/// <para>Pure and platform-free so the ordering rules can be tested without a microphone.</para>
///
/// <para><b>Thread-safe.</b> The recogniser delivers results and hypotheses on WinRT threads while
/// the stop sequence reads the transcript on the UI thread. Detaching the handlers before the
/// flush narrows that window but does not close it — an event already being dispatched still
/// runs — and a <see cref="List{T}"/> mutated during a read can throw or lose entries. The lock
/// is uncontended in practice and guards a few string operations, so its cost is irrelevant next
/// to being able to reason about it.</para>
/// </summary>
public sealed class DictationTranscript
{
    private readonly Lock _gate = new();
    private readonly List<string> _utterances = [];
    private string _pendingHypothesis = "";

    /// <summary>True when nothing was heard, so the caller can skip touching the composer.</summary>
    public bool IsEmpty
    {
        get { lock (_gate) return _utterances.Count == 0 && string.IsNullOrWhiteSpace(_pendingHypothesis); }
    }

    /// <summary>Discards anything from a previous session. Called when recording starts.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            _utterances.Clear();
            _pendingHypothesis = "";
        }
    }

    /// <summary>
    /// Records a finalised utterance.
    /// </summary>
    /// <remarks>Clearing the pending hypothesis is what stops the same words being inserted
    /// twice: the hypothesis that produced this result is now superseded by it, and a stale copy
    /// would otherwise be flushed again by <see cref="BuildInsertion"/>.</remarks>
    public void AddResult(string utterance)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(utterance)) _utterances.Add(utterance.Trim());
            _pendingHypothesis = "";
        }
    }

    /// <summary>
    /// Records the recogniser's current guess at the utterance in progress, replacing any previous
    /// guess.
    /// </summary>
    /// <remarks>Kept because stopping cancels the session, and cancellation can end it before the
    /// recogniser finalises what it was hearing. Without this the user's last sentence — often the
    /// whole of a short dictation — would be lost.</remarks>
    public void SetHypothesis(string text)
    {
        lock (_gate) _pendingHypothesis = text ?? "";
    }

    /// <summary>
    /// Builds the composer's new text: everything heard, appended to what is already there.
    /// </summary>
    /// <param name="existingText">The composer's content read at stop time, so anything the user
    /// typed during recording is preserved and kept in front of the dictated text.</param>
    public string BuildInsertion(string existingText)
    {
        lock (_gate)
        {
            var spoken = "";
            foreach (var utterance in _utterances) spoken = Join(spoken, utterance);
            spoken = Join(spoken, _pendingHypothesis.Trim());

            return Join(existingText ?? "", spoken);
        }
    }

    /// <summary>Joins two fragments with exactly one separating space, tolerating either side
    /// being empty or padded.</summary>
    public static string Join(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(right)) return left ?? "";
        if (string.IsNullOrWhiteSpace(left)) return right.TrimStart();
        return $"{left.TrimEnd()} {right.TrimStart()}";
    }
}
