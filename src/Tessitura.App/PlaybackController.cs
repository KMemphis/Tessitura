using System.Collections.Immutable;
using MeltySynth;
using Tessitura.Core;
using Tessitura.Playback.Audio;
using Tessitura.Playback.Performance;

namespace Tessitura.App;

/// <summary>Plays the current score and keeps the score cursor on the audible position.</summary>
public sealed class PlaybackController : IDisposable
{
    private readonly ScoreInputController _input;
    private readonly Func<int, Synthesizer> _createSynthesizer;
    private readonly Func<IAudioOutput> _createOutput;
    private Sequencer? _sequencer;
    private IAudioOutput? _output;
    private TempoMap? _tempo;
    private int _sampleRate;

    /// <summary>Creates a controller.</summary>
    /// <param name="input">The editor whose score is played and whose cursor follows the sound.</param>
    /// <param name="createSynthesizer">Creates the synthesizer for a sample rate, with the chosen SoundFont.</param>
    /// <param name="createOutput">Opens the audio device.</param>
    public PlaybackController(ScoreInputController input, Func<int, Synthesizer> createSynthesizer, Func<IAudioOutput> createOutput)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _createSynthesizer = createSynthesizer ?? throw new ArgumentNullException(nameof(createSynthesizer));
        _createOutput = createOutput ?? throw new ArgumentNullException(nameof(createOutput));
        _input.StateChanged += OnScoreChanged;
        _lastScore = input.CurrentScore;
    }

    private Score _lastScore;

    /// <summary>Gets whether the score is playing.</summary>
    public bool IsPlaying => _sequencer?.IsPlaying == true;

    /// <summary>Gets the audible position in samples, or zero when nothing has played.</summary>
    public long PositionSamples => _sequencer?.PositionSamples ?? 0;

    /// <summary>Defines the playback actions.</summary>
    /// <returns>The action definitions.</returns>
    public ImmutableArray<ActionDefinition> CreateActions() =>
    [
        new("playback.toggle", "Reproducir o detener", "Space", Toggle),
        new("playback.stop", "Detener y volver al inicio", "Ctrl+Space", StopAndRewind),
    ];

    /// <summary>Starts from the cursor, or stops when already playing.</summary>
    public void Toggle()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Start();
        }
    }

    /// <summary>Starts playing from the input cursor.</summary>
    public void Start()
    {
        EnsureEngine();
        Rebuild();
        _sequencer!.Seek(SamplesAt(_input.Cursor.Position));
        _sequencer.StartPlaying();
    }

    /// <summary>Pauses, leaving the cursor where the sound stopped.</summary>
    public void Pause()
    {
        _sequencer?.PausePlaying();
        Follow();
    }

    /// <summary>Stops and returns the cursor to the start.</summary>
    public void StopAndRewind()
    {
        _sequencer?.PausePlaying();
        _sequencer?.Seek(0);
        _input.SetCursorPosition(Fraction.Zero);
    }

    /// <summary>Moves the cursor to the audible position; call once per display frame.</summary>
    public void Tick()
    {
        if (_sequencer is null)
        {
            return;
        }

        Follow();
        if (!_sequencer.IsPlaying && _output is not null)
        {
            _output.Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _input.StateChanged -= OnScoreChanged;
        _output?.Dispose();
    }

    private void Follow()
    {
        if (_sequencer is null || _tempo is null)
        {
            return;
        }

        // The device plays what was rendered one buffer ago, so the head lags the sequencer clock by the buffer.
        double seconds = (double)_sequencer.PositionSamples / _sampleRate - (_output?.BufferMilliseconds ?? 0) / 1000.0;
        _input.SetCursorPosition(_tempo.PositionAt(Math.Max(0, seconds)));
    }

    private void EnsureEngine()
    {
        if (_output is null)
        {
            _output = _createOutput();
            _sampleRate = _output.SampleRate;
            _sequencer = new Sequencer(_createSynthesizer(_sampleRate));
        }

        if (_output is not null)
        {
            _output.Start(_sequencer!);
        }
    }

    private void Rebuild()
    {
        Interpretation interpretation = Interpreter.Interpret(_input.CurrentScore);
        _tempo = interpretation.Tempo;
        _sequencer!.Load(SequenceData.Build(interpretation, _sampleRate));
        _lastScore = _input.CurrentScore;
    }

    private long SamplesAt(Fraction position) => (long)Math.Round(_tempo!.SecondsAt(position) * _sampleRate);

    // An edit while playing reprograms the sequencer without stopping the sound.
    private void OnScoreChanged(object? sender, EventArgs e)
    {
        if (_sequencer is not null && IsPlaying && !ReferenceEquals(_lastScore, _input.CurrentScore))
        {
            Rebuild();
        }
    }
}
