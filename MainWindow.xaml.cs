using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PocStt.Configuration;
using PocStt.Speech;

namespace PocStt;

/// <summary>
/// Janela principal da PoC de Speech-to-Text.
///
/// A janela não conhece os detalhes de cada motor: fala apenas com
/// <see cref="ISpeechToTextEngine"/>. O motor ativo (Windows on-line, Windows
/// offline ou Whisper) é escolhido em runtime pelo ComboBox e configurado via
/// <c>config.json</c>.
///
/// Os eventos dos motores chegam em threads de segundo plano; cada atualização de
/// UI é re-despachada para a thread de UI via <see cref="DispatcherQueue"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly StringBuilder _committedText = new();
    private readonly AppSettings _settings;

    private ISpeechToTextEngine? _engine;
    private SttEngineKind _selectedKind;
    private Storyboard? _pulseStoryboard;

    private bool _isListening;
    private bool _isBusy;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _selectedKind = _settings.DefaultEngine;

        Closed += OnWindowClosed;

        SelectEngineInCombo(_selectedKind);
        UpdateUiForIdleState();
        SetStatus("Pronto.");
    }

    // =====================================================================
    //  Seleção de motor
    // =====================================================================

    private async void EngineSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isListening || _isBusy)
        {
            return; // não troca de motor durante uma sessão.
        }

        if (EngineSelector.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag ||
            !Enum.TryParse(tag, out SttEngineKind kind))
        {
            return;
        }

        _selectedKind = kind;
        // Status é definido de forma síncrona (antes do await) para não competir
        // com o "Pronto." inicial definido no construtor.
        SetStatus($"Motor selecionado: {tag}.");

        // Trocar de motor exige recriar a instância — descarta a atual (ociosa).
        await DisposeEngineAsync();
    }

    private void SelectEngineInCombo(SttEngineKind kind)
    {
        foreach (object obj in EngineSelector.Items)
        {
            if (obj is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, out SttEngineKind k) &&
                k == kind)
            {
                EngineSelector.SelectedItem = item;
                return;
            }
        }

        if (EngineSelector.Items.Count > 0)
        {
            EngineSelector.SelectedIndex = 0;
        }
    }

    // =====================================================================
    //  Interação do usuário
    // =====================================================================

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_isListening)
        {
            await StopListeningAsync();
        }
        else
        {
            await StartListeningAsync();
        }
    }

    private async Task StartListeningAsync()
    {
        _isBusy = true;
        RecordButton.IsEnabled = false;

        try
        {
            SetStatus("Preparando o motor...");

            if (!await SttErrors.IsMicrophonePresentAsync())
            {
                SetStatus("Nenhum microfone foi detectado. Conecte um dispositivo de captura e tente novamente.");
                return;
            }

            ISpeechToTextEngine engine = GetOrCreateEngine();
            await engine.InitializeAsync();
            await engine.StartAsync();

            _isListening = true;
            UpdateUiForListeningState();
        }
        catch (Exception ex)
        {
            SetStatus(SttErrors.Describe(ex));
            await DisposeEngineAsync(); // recria limpo na próxima tentativa.
            _isListening = false;
            UpdateUiForIdleState();
        }
        finally
        {
            _isBusy = false;
            RecordButton.IsEnabled = true;
        }
    }

    private async Task StopListeningAsync()
    {
        if (_engine is null)
        {
            return;
        }

        _isBusy = true;
        RecordButton.IsEnabled = false;

        try
        {
            SetStatus("Finalizando...");
            await _engine.StopAsync();
            SetStatus("Pronto.");
        }
        catch (Exception ex)
        {
            SetStatus(SttErrors.Describe(ex));
        }
        finally
        {
            _isListening = false;
            UpdateUiForIdleState();
            _isBusy = false;
            RecordButton.IsEnabled = true;
        }
    }

    // =====================================================================
    //  Fábrica e ligação de eventos do motor
    // =====================================================================

    private ISpeechToTextEngine GetOrCreateEngine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        ISpeechToTextEngine engine = CreateEngine(_selectedKind);
        AttachHandlers(engine);
        _engine = engine;
        return engine;
    }

    private ISpeechToTextEngine CreateEngine(SttEngineKind kind) => kind switch
    {
        SttEngineKind.WindowsOnlineDictation => new WindowsDictationEngine(_settings.Language),
        SttEngineKind.WindowsOffline => new WindowsOfflineEngine(_settings.Language, _settings.WindowsOffline.Commands),
        SttEngineKind.Whisper => new WhisperEngine(_settings.Whisper),
        _ => new WindowsDictationEngine(_settings.Language)
    };

    private void AttachHandlers(ISpeechToTextEngine engine)
    {
        engine.PartialRecognized += (_, a) =>
            _dispatcherQueue.TryEnqueue(() => HypothesisTextBlock.Text = a.Text);

        engine.FinalRecognized += (_, a) =>
            _dispatcherQueue.TryEnqueue(() => AppendFinalText(a.Text));

        engine.StatusChanged += (_, a) =>
            _dispatcherQueue.TryEnqueue(() => SetStatus(a.Message));

        engine.ErrorOccurred += (_, a) =>
            _dispatcherQueue.TryEnqueue(() => SetStatus(a.Message));

        engine.Stopped += (_, _) =>
            _dispatcherQueue.TryEnqueue(OnEngineStopped);
    }

    private void OnEngineStopped()
    {
        if (_isClosing)
        {
            return;
        }

        _isListening = false;
        UpdateUiForIdleState();
    }

    private void AppendFinalText(string text)
    {
        if (_committedText.Length > 0)
        {
            _committedText.Append(' ');
        }
        _committedText.Append(text);

        TranscriptionTextBox.Text = _committedText.ToString();
        TranscriptionTextBox.SelectionStart = TranscriptionTextBox.Text.Length;
        TranscriptionTextBox.SelectionLength = 0;

        HypothesisTextBlock.Text = string.Empty;
    }

    // =====================================================================
    //  Atualizações de UI
    // =====================================================================

    private void SetStatus(string message) => StatusTextBlock.Text = message;

    private void UpdateUiForListeningState()
    {
        RecordButton.Content = "■  Parar";
        RecordButton.Background = (Brush)RootGrid.Resources["RecordingBrush"];
        ListeningIndicator.Visibility = Visibility.Visible;
        EngineSelector.IsEnabled = false;
        StartPulse();
        SetStatus("Ouvindo...");
    }

    private void UpdateUiForIdleState()
    {
        // Apenas restaura os controles visuais. NÃO altera o status, para preservar
        // a mensagem de erro/motivo definida por quem chamou (ex.: falha ao iniciar
        // ou "Reconhecimento interrompido: ..." vindo do motor).
        RecordButton.Content = "🎙  Iniciar gravação";
        RecordButton.ClearValue(Control.BackgroundProperty);
        ListeningIndicator.Visibility = Visibility.Collapsed;
        EngineSelector.IsEnabled = true;
        StopPulse();
        HypothesisTextBlock.Text = string.Empty;
    }

    private void StartPulse()
    {
        _pulseStoryboard ??= (Storyboard)RootGrid.Resources["PulseStoryboard"];
        _pulseStoryboard.Begin();
    }

    private void StopPulse()
    {
        _pulseStoryboard?.Stop();
        ListeningDot.Opacity = 1.0;
    }

    // =====================================================================
    //  Teardown
    // =====================================================================

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isClosing = true;
        Closed -= OnWindowClosed;
        await DisposeEngineAsync();
    }

    private async Task DisposeEngineAsync()
    {
        ISpeechToTextEngine? engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            try
            {
                await engine.DisposeAsync();
            }
            catch
            {
                // Nada acionável durante o teardown.
            }
        }
    }
}
