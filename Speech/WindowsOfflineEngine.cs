using System.Collections.Generic;
using System.Linq;
using Windows.Media.SpeechRecognition;

namespace PocStt.Speech;

/// <summary>
/// Motor OFFLINE do Windows: <see cref="SpeechRecognitionListConstraint"/> com uma
/// lista de comandos. Roda 100% on-device, sem internet e sem o toggle de
/// "Reconhecimento de fala on-line". Reconhece apenas o que está na lista.
/// </summary>
public sealed class WindowsOfflineEngine : WindowsSpeechEngineBase
{
    private readonly IReadOnlyList<string> _commands;

    public WindowsOfflineEngine(string languageTag, IEnumerable<string> commands)
        : base(languageTag)
    {
        _commands = commands?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList()
                    ?? new List<string>();
    }

    public override string DisplayName => "Windows — offline (lista de comandos)";

    // Lista de comandos não gera hipóteses; só resultados finais.
    public override bool SupportsPartialResults => false;

    protected override void AddConstraints(SpeechRecognizer recognizer)
    {
        recognizer.Constraints.Add(new SpeechRecognitionListConstraint(_commands, "commands"));
    }
}
