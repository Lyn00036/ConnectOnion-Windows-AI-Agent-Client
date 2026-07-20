using ConnectOnion.WinUIClient.Services.Speech;

namespace ConnectOnion.WinUIClient.UnitTests.Services;

public sealed class DictationTranscriptTests
{
    private static DictationTranscript Started()
    {
        var transcript = new DictationTranscript();
        transcript.Begin();
        return transcript;
    }

    [Fact]
    public void BuildInsertion_ConcatenatesEveryFinalisedUtteranceInOrder()
    {
        var transcript = Started();
        transcript.AddResult("one");
        transcript.AddResult("two");
        transcript.AddResult("three");

        Assert.Equal("one two three", transcript.BuildInsertion(""));
    }

    [Fact]
    public void BuildInsertion_KeepsWhatTheUserTyped_AndPutsSpeechAfterIt()
    {
        // The composer is untouched during recording, so the user can type freely; what they
        // typed is read at stop and must survive.
        var transcript = Started();
        transcript.AddResult("spoken");

        Assert.Equal("typed while recording spoken",
            transcript.BuildInsertion("typed while recording"));
    }

    [Fact]
    public void BuildInsertion_TrailingHypothesis_IsIncluded()
    {
        // Stopping cancels the session, which can end it before the recogniser finalises the last
        // utterance. For a short dictation that utterance is the entire transcript.
        var transcript = Started();
        transcript.SetHypothesis("never finalised");

        Assert.Equal("never finalised", transcript.BuildInsertion(""));
    }

    [Fact]
    public void BuildInsertion_HypothesisSupersededByItsResult_IsNotInsertedTwice()
    {
        var transcript = Started();
        transcript.SetHypothesis("hello wor");
        transcript.AddResult("hello world");

        Assert.Equal("hello world", transcript.BuildInsertion(""));
    }

    [Fact]
    public void BuildInsertion_ResultsThenANewHypothesis_KeepsBoth()
    {
        var transcript = Started();
        transcript.AddResult("first sentence");
        transcript.SetHypothesis("second one in flight");

        Assert.Equal("first sentence second one in flight", transcript.BuildInsertion(""));
    }

    [Fact]
    public void SetHypothesis_RevisedGuess_ReplacesThePreviousOne()
    {
        var transcript = Started();
        transcript.SetHypothesis("wreck a nice");
        transcript.SetHypothesis("recognise");

        Assert.Equal("recognise", transcript.BuildInsertion(""));
    }

    [Fact]
    public void IsEmpty_NothingHeard_SoTheComposerIsNeverTouched()
    {
        var transcript = Started();
        Assert.True(transcript.IsEmpty);

        transcript.SetHypothesis("something");
        Assert.False(transcript.IsEmpty);
    }

    [Fact]
    public void IsEmpty_IgnoresWhitespaceOnlyInput()
    {
        var transcript = Started();
        transcript.AddResult("   ");
        transcript.SetHypothesis("  ");

        Assert.True(transcript.IsEmpty);
        Assert.Equal("existing", transcript.BuildInsertion("existing"));
    }

    [Fact]
    public void Begin_DiscardsThePreviousSession()
    {
        var transcript = Started();
        transcript.AddResult("old session");
        transcript.SetHypothesis("also old");

        transcript.Begin();

        Assert.True(transcript.IsEmpty);
        Assert.Equal("", transcript.BuildInsertion(""));
    }

    [Fact]
    public void BuildInsertion_DoesNotConsumeTheTranscript()
    {
        // Called once in practice, but a second call must not produce different text — a flush
        // that silently emptied itself would be a trap for any later caller.
        var transcript = Started();
        transcript.AddResult("stable");

        Assert.Equal("stable", transcript.BuildInsertion(""));
        Assert.Equal("stable", transcript.BuildInsertion(""));
    }

    [Theory]
    [InlineData("", "b", "b")]
    [InlineData("a", "", "a")]
    [InlineData("a", "b", "a b")]
    [InlineData("a ", " b", "a b")]
    [InlineData("", "", "")]
    [InlineData("a", "   ", "a")]
    public void Join_UsesExactlyOneSpace_AndToleratesEmptySides(string left, string right, string expected)
        => Assert.Equal(expected, DictationTranscript.Join(left, right));

    [Fact]
    public async Task ConcurrentCallbacksAndFlush_DoNotThrowOrLoseUtterances()
    {
        // The recogniser delivers on WinRT threads while the stop sequence reads on the UI thread;
        // detaching handlers before the flush narrows that window without closing it.
        var transcript = Started();
        using var cts = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                transcript.AddResult($"u{i}");
                transcript.SetHypothesis($"h{i}");
            }
        });

        var reader = Task.Run(() =>
        {
            while (!writer.IsCompleted) _ = transcript.BuildInsertion("base");
        });

        await Task.WhenAll(writer, reader);

        var final = transcript.BuildInsertion("base");
        Assert.StartsWith("base u0 u1 ", final, StringComparison.Ordinal);
        Assert.Contains("u1999", final, StringComparison.Ordinal);
    }
}
