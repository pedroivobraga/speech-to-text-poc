using System;
using System.Threading.Tasks;

namespace PocStt.Speech;

/// <summary>Texto reconhecido (parcial ou final).</summary>
public sealed class SttTextEventArgs : EventArgs
{
    public SttTextEventArgs(string text) => Text = text;
    public string Text { get; }
}

/// <summary>Mensagem de status para a barra inferior.</summary>
public sealed class SttStatusEventArgs : EventArgs
{
    public SttStatusEventArgs(string message) => Message = message;
    public string Message { get; }
}

/// <summary>Erro tratável, já com mensagem amigável.</summary>
public sealed class SttErrorEventArgs : EventArgs
{
    public SttErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }

    public string Message { get; }
    public Exception? Exception { get; }
}

/// <summary>
/// Contrato comum a todos os motores de Speech-to-Text (Windows on-line, Windows
/// offline e Whisper). Permite trocar o motor em runtime sem alterar a UI.
///
/// IMPORTANTE: os eventos podem ser disparados em threads de SEGUNDO PLANO.
/// Quem consome (a janela) é responsável por re-despachar para a thread de UI.
/// </summary>
public interface ISpeechToTextEngine : IAsyncDisposable
{
    /// <summary>Nome amigável para exibição/diagnóstico.</summary>
    string DisplayName { get; }

    /// <summary>Indica se o motor emite hipóteses parciais (feedback ao vivo).</summary>
    bool SupportsPartialResults { get; }

    /// <summary>Hipótese parcial (antes da consolidação). Pode não ocorrer em todos os motores.</summary>
    event EventHandler<SttTextEventArgs>? PartialRecognized;

    /// <summary>Trecho final consolidado da transcrição.</summary>
    event EventHandler<SttTextEventArgs>? FinalRecognized;

    /// <summary>Atualização de status (ex.: "Ouvindo...", "Transcrevendo...").</summary>
    event EventHandler<SttStatusEventArgs>? StatusChanged;

    /// <summary>Erro tratável, com mensagem pronta para exibição.</summary>
    event EventHandler<SttErrorEventArgs>? ErrorOccurred;

    /// <summary>O motor parou por conta própria (fim de sessão ou erro fatal).</summary>
    event EventHandler? Stopped;

    /// <summary>Prepara o motor (compila gramática / carrega modelo). Idempotente.</summary>
    Task InitializeAsync();

    /// <summary>Inicia a captura/reconhecimento contínuo.</summary>
    Task StartAsync();

    /// <summary>Para a captura e consolida o que faltava.</summary>
    Task StopAsync();
}
