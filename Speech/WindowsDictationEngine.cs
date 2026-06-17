using Windows.Media.SpeechRecognition;

namespace PocStt.Speech;

/// <summary>
/// Motor on-line do Windows: gramática de DITADO (fala livre). Usa o serviço de
/// fala on-line — exige microfone, "Reconhecimento de fala on-line" ativado e
/// (na prática) conectividade. Suporta hipóteses parciais ao vivo.
/// </summary>
public sealed class WindowsDictationEngine : WindowsSpeechEngineBase
{
    public WindowsDictationEngine(string languageTag) : base(languageTag)
    {
    }

    public override string DisplayName => "Windows — ditado on-line (fala livre)";

    public override bool SupportsPartialResults => true;

    protected override void AddConstraints(SpeechRecognizer recognizer)
    {
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
            SpeechRecognitionScenario.Dictation, "dictation"));
    }
}
