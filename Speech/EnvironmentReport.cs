using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocStt.Configuration;
using Whisper.net;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using Windows.Storage;

namespace PocStt.Speech;

/// <summary>
/// Monta um relatório legível do ambiente de reconhecimento de voz: versões,
/// idiomas suportados (ditado on-line e gramática offline), microfones presentes e
/// a configuração efetiva do Whisper (inclusive se o modelo já está em disco).
/// Cada seção é defensiva: uma consulta que falhe não derruba o relatório inteiro.
/// </summary>
public static class EnvironmentReport
{
    public static async Task<string> BuildAsync(AppSettings settings, SttEngineKind selectedKind)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Selected engine : {selectedKind}");
        sb.AppendLine($"Whisper.net     : {SafeWhisperVersion()}");
        sb.AppendLine($"Config language : {settings.Language}");
        sb.AppendLine();

        // --- Idiomas do reconhecedor do Windows ---
        try
        {
            Language system = SpeechRecognizer.SystemSpeechLanguage;
            sb.AppendLine($"System speech language : {system.DisplayName} ({system.LanguageTag})");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"System speech language : (unavailable: {ex.Message})");
        }

        try
        {
            var topic = SpeechRecognizer.SupportedTopicLanguages.ToList();
            bool configSupported = topic.Any(l =>
                string.Equals(l.LanguageTag, settings.Language, StringComparison.OrdinalIgnoreCase));

            sb.AppendLine($"Dictation (online) langs: {topic.Count} " +
                          $"[{string.Join(", ", topic.Take(8).Select(l => l.LanguageTag))}" +
                          (topic.Count > 8 ? ", ..." : "") + "]");
            sb.AppendLine($"  '{settings.Language}' supports dictation: {(configSupported ? "YES" : "NO")}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Dictation (online) langs: (unavailable: {ex.Message})");
        }

        try
        {
            var grammar = SpeechRecognizer.SupportedGrammarLanguages.ToList();
            sb.AppendLine($"Grammar (offline) langs : {grammar.Count} " +
                          $"[{string.Join(", ", grammar.Take(8).Select(l => l.LanguageTag))}" +
                          (grammar.Count > 8 ? ", ..." : "") + "]");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Grammar (offline) langs : (unavailable: {ex.Message})");
        }

        // --- Microfones ---
        try
        {
            DeviceInformationCollection mics =
                await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            string defaultName = mics.FirstOrDefault(d => d.IsDefault)?.Name
                                 ?? mics.FirstOrDefault()?.Name ?? "(none)";
            sb.AppendLine($"Microphones     : {mics.Count} | default: {defaultName}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Microphones     : (unavailable: {ex.Message})");
        }

        // --- Whisper ---
        sb.AppendLine();
        sb.AppendLine("Whisper:");
        WhisperSettings w = settings.Whisper;
        sb.AppendLine($"  ggmlType    : {w.GgmlType}");
        sb.AppendLine($"  language    : {w.Language}");
        sb.AppendLine($"  chunkSeconds: {w.ChunkSeconds}");
        try
        {
            string modelPath = ResolveModelPath(w);
            sb.AppendLine($"  model path  : {modelPath}");
            sb.AppendLine($"  model on disk: {(File.Exists(modelPath) ? "YES" : "NO (will download on first run)")}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  model path  : (unavailable: {ex.Message})");
        }

        sb.AppendLine();
        sb.AppendLine("Note: the \"Online speech recognition\" toggle cannot be read by the app —");
        sb.AppendLine("verify it in Settings > Privacy & security > Speech.");

        return sb.ToString().TrimEnd();
    }

    private static string SafeWhisperVersion()
    {
        try
        {
            return typeof(WhisperFactory).Assembly.GetName().Version?.ToString() ?? "(unknown)";
        }
        catch
        {
            return "(unknown)";
        }
    }

    private static string ResolveModelPath(WhisperSettings w)
    {
        if (!string.IsNullOrWhiteSpace(w.ModelFilePath))
        {
            return w.ModelFilePath;
        }

        string directory = string.IsNullOrWhiteSpace(w.ModelDirectory)
            ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "models")
            : w.ModelDirectory;

        string ggml = string.IsNullOrWhiteSpace(w.GgmlType) ? "base" : w.GgmlType;
        return Path.Combine(directory, $"ggml-{ggml}.bin");
    }
}
