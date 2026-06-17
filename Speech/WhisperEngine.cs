using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using PocStt.Configuration;
using Whisper.net;
using Windows.Storage;

namespace PocStt.Speech;

/// <summary>
/// Motor LOCAL baseado em Whisper.net. Captura o microfone com NAudio (16 kHz,
/// mono), acumula áudio e transcreve em blocos de N segundos com o modelo ggml —
/// tudo on-device. Fala livre, sem enviar áudio para a nuvem.
///
/// Observação: o Whisper não é streaming nativo; processamos em blocos. Isso pode
/// cortar palavras na fronteira do bloco — aumente <c>chunkSeconds</c> para mais
/// contexto. O download do modelo (1ª vez) precisa de internet; o reconhecimento, não.
/// </summary>
public sealed class WhisperEngine : ISpeechToTextEngine
{
    private const int SampleRate = 16000; // exigido pelo Whisper.

    private readonly WhisperSettings _settings;
    private readonly object _bufferLock = new();
    private readonly List<float> _buffer = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WaveInEvent? _waveIn;
    private CancellationTokenSource? _cts;
    private Task? _processingLoop;
    private bool _disposed;

    public WhisperEngine(WhisperSettings settings) => _settings = settings;

    public string DisplayName => "Whisper — local (fala livre)";

    // O Whisper não emite hipóteses parciais; apenas blocos finais.
    public bool SupportsPartialResults => false;

    // Whisper não emite hipóteses parciais; o evento existe só para cumprir a interface.
#pragma warning disable CS0067
    public event EventHandler<SttTextEventArgs>? PartialRecognized;
#pragma warning restore CS0067
    public event EventHandler<SttTextEventArgs>? FinalRecognized;
    public event EventHandler<SttStatusEventArgs>? StatusChanged;
    public event EventHandler<SttErrorEventArgs>? ErrorOccurred;
    public event EventHandler? Stopped;

    // ----------------------------------------------------------- inicialização

    public async Task InitializeAsync()
    {
        if (_processor is not null || _disposed)
        {
            return;
        }

        string modelPath = await EnsureModelAsync();

        Status("Carregando modelo Whisper...");
        // Carregar o modelo é síncrono e pesado: fora da thread de UI.
        WhisperFactory factory = await Task.Run(() => WhisperFactory.FromPath(modelPath));
        WhisperProcessor processor = factory.CreateBuilder()
            .WithLanguage(string.IsNullOrWhiteSpace(_settings.Language) ? "auto" : _settings.Language)
            .Build();

        _factory = factory;
        _processor = processor;
        Status("Whisper pronto.");
    }

    // -------------------------------------------------------------- captura

    public Task StartAsync()
    {
        if (_processor is null)
        {
            throw new InvalidOperationException("Whisper não inicializado. Chame InitializeAsync primeiro.");
        }

        lock (_bufferLock)
        {
            _buffer.Clear();
        }

        _cts = new CancellationTokenSource();

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1), // 16 kHz, 16-bit, mono
            BufferMilliseconds = 200
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();

        _processingLoop = Task.Run(() => ProcessLoopAsync(_cts.Token));

        Status("Ouvindo (Whisper)...");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        // Para a captura primeiro.
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            try { _waveIn.StopRecording(); } catch { /* ignore */ }
            _waveIn.Dispose();
            _waveIn = null;
        }

        // Encerra o loop de processamento.
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_processingLoop is not null)
            {
                try { await _processingLoop; } catch { /* cancelado */ }
            }
            _cts.Dispose();
            _cts = null;
            _processingLoop = null;
        }

        // Transcreve o que sobrou no buffer.
        float[] remaining;
        lock (_bufferLock)
        {
            remaining = _buffer.ToArray();
            _buffer.Clear();
        }
        if (remaining.Length >= SampleRate / 2) // ao menos 0,5 s
        {
            try
            {
                await TranscribeAsync(remaining, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new SttErrorEventArgs($"Falha ao finalizar a transcrição: {ex.Message}", ex));
            }
        }

        Status("Parado.");
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Converte PCM 16-bit (little-endian) para float [-1, 1].
        int sampleCount = e.BytesRecorded / 2;
        lock (_bufferLock)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                _buffer.Add(sample / 32768f);
            }
        }
    }

    private async Task ProcessLoopAsync(CancellationToken token)
    {
        int chunkSamples = SampleRate * Math.Max(1, _settings.ChunkSeconds);

        try
        {
            while (!token.IsCancellationRequested)
            {
                float[]? chunk = null;
                lock (_bufferLock)
                {
                    if (_buffer.Count >= chunkSamples)
                    {
                        chunk = _buffer.GetRange(0, chunkSamples).ToArray();
                        _buffer.RemoveRange(0, chunkSamples);
                    }
                }

                if (chunk is null)
                {
                    await Task.Delay(100, token);
                    continue;
                }

                await TranscribeAsync(chunk, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Parada normal.
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new SttErrorEventArgs($"Erro no Whisper: {ex.Message}", ex));
        }
    }

    private async Task TranscribeAsync(float[] samples, CancellationToken token)
    {
        if (_processor is null)
        {
            return;
        }

        Status("Transcrevendo (Whisper)...");
        var builder = new StringBuilder();
        await foreach (SegmentData segment in _processor.ProcessAsync(samples, token))
        {
            builder.Append(segment.Text);
        }

        string text = builder.ToString().Trim();
        if (text.Length > 0)
        {
            FinalRecognized?.Invoke(this, new SttTextEventArgs(text));
        }
    }

    // ------------------------------------------------------------- modelo

    /// <summary>Garante o arquivo do modelo localmente; baixa do Hugging Face se preciso.</summary>
    private async Task<string> EnsureModelAsync()
    {
        // 1) Caminho explícito tem prioridade.
        if (!string.IsNullOrWhiteSpace(_settings.ModelFilePath) && File.Exists(_settings.ModelFilePath))
        {
            return _settings.ModelFilePath;
        }

        // 2) Resolve diretório de modelos (padrão: LocalFolder\models do pacote).
        string directory = string.IsNullOrWhiteSpace(_settings.ModelDirectory)
            ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "models")
            : _settings.ModelDirectory;
        Directory.CreateDirectory(directory);

        string ggml = string.IsNullOrWhiteSpace(_settings.GgmlType) ? "base" : _settings.GgmlType;
        string modelFile = Path.Combine(directory, $"ggml-{ggml}.bin");
        if (File.Exists(modelFile))
        {
            return modelFile;
        }

        if (!_settings.AutoDownloadModel)
        {
            throw new FileNotFoundException(
                $"Modelo Whisper não encontrado: {modelFile}. Baixe 'ggml-{ggml}.bin' " +
                "manualmente ou habilite 'autoDownloadModel' no config.json.");
        }

        // 3) Download (1ª vez requer internet).
        string url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{ggml}.bin";
        Status($"Baixando modelo Whisper '{ggml}' (pode demorar na 1ª vez)...");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using HttpResponseMessage response =
            await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string tempFile = modelFile + ".part";
        await using (FileStream fs = File.Create(tempFile))
        await using (Stream src = await response.Content.ReadAsStreamAsync())
        {
            await src.CopyToAsync(fs);
        }
        File.Move(tempFile, modelFile, overwrite: true);

        Status("Modelo Whisper baixado.");
        return modelFile;
    }

    private void Status(string message) =>
        StatusChanged?.Invoke(this, new SttStatusEventArgs(message));

    // ------------------------------------------------------------- teardown

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            await StopAsync();
        }
        catch
        {
            // ignore durante teardown
        }

        if (_processor is not null)
        {
            await _processor.DisposeAsync();
            _processor = null;
        }
        _factory?.Dispose();
        _factory = null;
    }
}
