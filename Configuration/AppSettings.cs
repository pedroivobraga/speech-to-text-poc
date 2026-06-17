using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocStt.Configuration;

/// <summary>Identifica o motor de reconhecimento de voz ativo.</summary>
public enum SttEngineKind
{
    /// <summary>Windows + gramática de ditado on-line (fala livre, usa a nuvem).</summary>
    WindowsOnlineDictation,

    /// <summary>Windows + lista de comandos (on-device, sem internet).</summary>
    WindowsOffline,

    /// <summary>Whisper.net local (fala livre, on-device).</summary>
    Whisper
}

/// <summary>Opções do motor offline do Windows (lista de comandos).</summary>
public sealed class WindowsOfflineSettings
{
    public IList<string> Commands { get; set; } = new List<string>
    {
        "iniciar", "parar", "salvar", "cancelar", "próximo", "anterior", "sim", "não"
    };
}

/// <summary>Opções do motor Whisper.</summary>
public sealed class WhisperSettings
{
    /// <summary>Modelo ggml: tiny, base, small, medium, large-v3, large-v3-turbo, etc.</summary>
    public string GgmlType { get; set; } = "base";

    /// <summary>Idioma do Whisper em ISO-639-1 ("pt", "en") ou "auto" para detectar.</summary>
    public string Language { get; set; } = "pt";

    /// <summary>Tamanho (em segundos) do bloco de áudio enviado ao Whisper por vez.</summary>
    public int ChunkSeconds { get; set; } = 5;

    /// <summary>Diretório do modelo. Vazio = LocalFolder\models do pacote.</summary>
    public string ModelDirectory { get; set; } = string.Empty;

    /// <summary>Caminho explícito de um .bin. Se informado e existir, ignora GgmlType.</summary>
    public string ModelFilePath { get; set; } = string.Empty;

    /// <summary>Baixa o modelo automaticamente do Hugging Face se ele não existir.</summary>
    public bool AutoDownloadModel { get; set; } = true;
}

/// <summary>
/// Configuração da aplicação, carregada de <c>config.json</c> (ao lado do executável).
/// Tudo aqui é ajustável sem recompilar.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Motor selecionado ao iniciar o app.</summary>
    public SttEngineKind DefaultEngine { get; set; } = SttEngineKind.WindowsOnlineDictation;

    /// <summary>Idioma BCP-47 para os motores do Windows (ex.: "pt-BR").</summary>
    public string Language { get; set; } = "pt-BR";

    public WindowsOfflineSettings WindowsOffline { get; set; } = new();

    public WhisperSettings Whisper { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    /// <summary>
    /// Carrega <c>config.json</c> do diretório do executável. Em qualquer falha
    /// (arquivo ausente ou inválido), retorna os valores padrão.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Configuração inválida não deve impedir o app de abrir.
        }

        return new AppSettings();
    }
}
