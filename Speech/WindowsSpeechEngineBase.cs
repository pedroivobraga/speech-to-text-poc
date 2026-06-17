using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace PocStt.Speech;

/// <summary>
/// Base comum aos motores que usam <see cref="SpeechRecognizer"/> do Windows.
/// Centraliza inicialização, sessão contínua, auto-restart em pausas longas,
/// roteamento de eventos e teardown. As subclasses só definem QUAL gramática usar.
/// </summary>
public abstract class WindowsSpeechEngineBase : ISpeechToTextEngine
{
    private readonly string _languageTag;
    private SpeechRecognizer? _recognizer;
    private volatile bool _stopRequested;
    private bool _disposed;

    protected WindowsSpeechEngineBase(string languageTag) => _languageTag = languageTag;

    public abstract string DisplayName { get; }

    /// <summary>Hipóteses só fazem sentido no ditado (não na lista de comandos).</summary>
    public virtual bool SupportsPartialResults => false;

    /// <summary>A subclasse adiciona as constraints (ditado, lista, gramática...).</summary>
    protected abstract void AddConstraints(SpeechRecognizer recognizer);

    public event EventHandler<SttTextEventArgs>? PartialRecognized;
    public event EventHandler<SttTextEventArgs>? FinalRecognized;
    public event EventHandler<SttStatusEventArgs>? StatusChanged;
    public event EventHandler<SttErrorEventArgs>? ErrorOccurred;
    public event EventHandler? Stopped;

    public async Task InitializeAsync()
    {
        if (_recognizer is not null || _disposed)
        {
            return;
        }

        SpeechRecognizer recognizer = CreateRecognizer(_languageTag);
        AddConstraints(recognizer);

        // Tolera a primeira pausa; pausas longas viram Completed/TimeoutExceeded
        // e são tratadas com auto-restart abaixo.
        recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
        recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(2);

        SpeechRecognitionCompilationResult compilation = await recognizer.CompileConstraintsAsync();
        if (compilation.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            throw new InvalidOperationException(DescribeCompilationFailure(compilation.Status));
        }

        recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
        recognizer.ContinuousRecognitionSession.Completed += OnCompleted;
        recognizer.StateChanged += OnStateChanged;
        if (SupportsPartialResults)
        {
            recognizer.HypothesisGenerated += OnHypothesisGenerated;
        }

        _recognizer = recognizer;
    }

    public async Task StartAsync()
    {
        await InitializeAsync();
        _stopRequested = false;
        await _recognizer!.ContinuousRecognitionSession.StartAsync();
    }

    public async Task StopAsync()
    {
        SpeechRecognizer? recognizer = _recognizer;
        if (recognizer is null)
        {
            return;
        }

        // Sinaliza parada ANTES, para que o auto-restart do Completed não dispare.
        _stopRequested = true;
        if (recognizer.State == SpeechRecognizerState.Idle)
        {
            return;
        }

        try
        {
            // StopAsync drena o áudio pendente, mas pode BLOQUEAR quando não há nada
            // a finalizar. Corremos contra um timeout e caímos para CancelAsync.
            Task stop = recognizer.ContinuousRecognitionSession.StopAsync().AsTask();
            if (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(3))) == stop)
            {
                await stop; // observa exceções
                return;
            }
        }
        catch
        {
            // cai para o cancelamento imediato abaixo.
        }

        try
        {
            await recognizer.ContinuousRecognitionSession.CancelAsync().AsTask();
        }
        catch
        {
            // nada acionável durante a parada.
        }
    }

    // ----------------------------------------------------------------- eventos

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Confidence == SpeechRecognitionConfidence.Rejected)
        {
            return;
        }

        string text = args.Result.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            FinalRecognized?.Invoke(this, new SttTextEventArgs(text));
        }
    }

    private void OnHypothesisGenerated(
        SpeechRecognizer sender,
        SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        PartialRecognized?.Invoke(this, new SttTextEventArgs(args.Hypothesis.Text));
    }

    private void OnStateChanged(
        SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
    {
        string? message = args.State switch
        {
            SpeechRecognizerState.Capturing => "Listening...",
            SpeechRecognizerState.SoundStarted => "Sound detected...",
            SpeechRecognizerState.SpeechDetected => "Speech detected...",
            SpeechRecognizerState.Processing => "Processing speech...",
            SpeechRecognizerState.Paused => "Paused.",
            _ => null
        };

        if (message is not null)
        {
            StatusChanged?.Invoke(this, new SttStatusEventArgs(message));
        }
    }

    private async void OnCompleted(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        // Parada intencional ou descarte: apenas sinaliza fim.
        if (_stopRequested || _disposed || _recognizer is null)
        {
            Stopped?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Pausa longa: reinicia de forma transparente (sessão verdadeiramente contínua).
        if (args.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
        {
            // Rechecagem: se o usuário pediu parada nesse meio-tempo, não reinicie
            // (evita Start/Stop concorrentes, que travam a sessão).
            if (_stopRequested || _disposed)
            {
                Stopped?.Invoke(this, EventArgs.Empty);
                return;
            }

            try
            {
                StatusChanged?.Invoke(this, new SttStatusEventArgs("Pause detected — still listening..."));
                await _recognizer.ContinuousRecognitionSession.StartAsync();
                return;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new SttErrorEventArgs(SttErrors.Describe(ex), ex));
            }
        }
        else if (args.Status != SpeechRecognitionResultStatus.Success)
        {
            StatusChanged?.Invoke(this, new SttStatusEventArgs($"Recognition interrupted: {args.Status}."));
        }

        Stopped?.Invoke(this, EventArgs.Empty);
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>Mensagem acionável para falhas de compilação da gramática.</summary>
    private static string DescribeCompilationFailure(SpeechRecognitionResultStatus status) => status switch
    {
        SpeechRecognitionResultStatus.TopicLanguageNotSupported =>
            "This language does not support online dictation. Turn on \"Online speech " +
            "recognition\" (Settings > Privacy & security > Speech) and install the speech " +
            "language pack — or use an offline/Whisper engine.",

        SpeechRecognitionResultStatus.GrammarLanguageMismatch =>
            "The grammar language does not match the recognizer language.",

        SpeechRecognitionResultStatus.GrammarCompilationFailure =>
            "Failed to compile the recognition grammar.",

        _ => $"Failed to prepare the recognizer (status: {status})."
    };

    /// <summary>
    /// Cria o reconhecedor no idioma pedido se suportado para ditado;
    /// caso contrário, usa o idioma de fala padrão do sistema.
    /// </summary>
    private static SpeechRecognizer CreateRecognizer(string languageTag)
    {
        try
        {
            var preferred = new Language(languageTag);
            bool supported = SpeechRecognizer.SupportedTopicLanguages.Any(
                l => string.Equals(l.LanguageTag, preferred.LanguageTag, StringComparison.OrdinalIgnoreCase));

            if (supported)
            {
                return new SpeechRecognizer(preferred);
            }
        }
        catch
        {
            // idioma inválido/indisponível -> padrão do sistema.
        }

        return new SpeechRecognizer();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;

        SpeechRecognizer? recognizer = _recognizer;
        _recognizer = null;
        if (recognizer is null)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            recognizer.ContinuousRecognitionSession.Completed -= OnCompleted;
            recognizer.StateChanged -= OnStateChanged;
            recognizer.HypothesisGenerated -= OnHypothesisGenerated;

            if (recognizer.State != SpeechRecognizerState.Idle)
            {
                _ = recognizer.ContinuousRecognitionSession.CancelAsync();
            }
        }
        catch
        {
            // Nada acionável durante o teardown.
        }
        finally
        {
            recognizer.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
