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
    private Mixer? _mixer;
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

    /// <summary>Gets the mixer; its changes act while the score plays.</summary>
    public Mixer Mixer => _mixer ??= new Mixer(_input.CurrentScore.Instruments.Length);

    /// <summary>Gets whether the score is playing.</summary>
    public bool IsPlaying => _sequencer?.IsPlaying == true;

    /// <summary>Gets the audible position in samples, or zero when nothing has played.</summary>
    public long PositionSamples => _sequencer?.PositionSamples ?? 0;

    /// <summary>Defines the playback actions.</summary>
    /// <returns>The action definitions.</returns>
    public ImmutableArray<ActionDefinition> CreateActions()
    {
        ImmutableArray<ActionDefinition>.Builder actions = ImmutableArray.CreateBuilder<ActionDefinition>();
        actions.Add(new("playback.toggle", "Reproducir o detener", "Space", Toggle));
        actions.Add(new("playback.stop", "Detener y volver al inicio", "Ctrl+Space", StopAndRewind));
        int count = _input.CurrentScore.Instruments.Length;
        for (int i = 0; i < Math.Min(count, 9); i++)
        {
            int instrument = i;
            string name = _input.CurrentScore.Instruments[i].Name;
            actions.Add(new($"mixer.mute.{i + 1}", $"Silenciar {name}", $"Alt+Shift+{i + 1}",
                () => Mixer.SetMute(instrument, !Mixer.IsMuted(instrument))));
            actions.Add(new($"mixer.solo.{i + 1}", $"Solo {name}", $"Ctrl+Shift+{i + 1}",
                () => Mixer.SetSolo(instrument, !Mixer.IsSoloed(instrument))));
            actions.Add(new($"mixer.volume-up.{i + 1}", $"Subir volumen de {name}", $"Alt+Ctrl+{i + 1}",
                () => Mixer.SetVolume(instrument, Mixer.GetVolume(instrument) + 0.1f)));
            actions.Add(new($"mixer.volume-down.{i + 1}", $"Bajar volumen de {name}", $"Alt+Ctrl+Shift+{i + 1}",
                () => Mixer.SetVolume(instrument, Mixer.GetVolume(instrument) - 0.1f)));
        }

        return actions.ToImmutable();
    }

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
            _sequencer.Attach(EnsureMixer());
        }

        if (_output is not null)
        {
            _output.Start(_sequencer!);
        }
    }

    private Mixer EnsureMixer() => Mixer;

    private void Rebuild()
    {
        Interpretation interpretation = Interpreter.Interpret(_input.CurrentScore);
        _tempo = interpretation.Tempo;
        if (Mixer.InstrumentCount != _input.CurrentScore.Instruments.Length)
        {
            _mixer = new Mixer(_input.CurrentScore.Instruments.Length);
            _sequencer!.Attach(_mixer);
        }

        // Each instrument gets the General MIDI program that matches its name.
        int[] programs = [.. _input.CurrentScore.Instruments.Select(i => GeneralMidiPrograms.FromName(i.Name))];
        _sequencer!.Load(SequenceData.Build(interpretation, _sampleRate, programs));
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
