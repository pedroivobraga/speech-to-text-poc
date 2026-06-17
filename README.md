# PoC — Reconhecimento de Voz (Speech-to-Text) · WinUI 3

Prova de conceito de **reconhecimento de voz contínuo e customizado** em um app
WinUI 3 **empacotado (MSIX)**, com **3 motores trocáveis em runtime**.

- ✅ Sem UI nativa: **o painel Win + H do Windows NÃO aparece**.
- ✅ Botão único centralizado (Iniciar/Parar) que muda de cor e texto.
- ✅ Transcrição contínua — tolera pausas (auto-restart em silêncio longo).
- ✅ Seletor de motor + tudo configurável por `config.json`.

## Motores disponíveis

| Motor | Tecnologia | Online? | Fala livre? |
|---|---|---|---|
| **Windows — ditado on-line** | `SpeechRecognitionTopicConstraint` (dictation) | Sim (nuvem MS) | Sim |
| **Windows — offline** | `SpeechRecognitionListConstraint` (comandos) | **Não** | Não (lista fixa) |
| **Whisper — local** | Whisper.net + NAudio (16 kHz) | **Não*** | Sim |

\* O Whisper baixa o modelo `ggml` na 1ª vez (precisa de internet só nesse momento);
o reconhecimento em si roda 100% on-device.

## Configuração (`config.json`)

Copiado para a saída e lido ao iniciar. Ajuste sem recompilar:

```json
{
  "defaultEngine": "WindowsOnlineDictation",   // ou "WindowsOffline" / "Whisper"
  "language": "pt-BR",                          // idioma dos motores do Windows
  "windowsOffline": { "commands": ["iniciar","parar","salvar","cancelar"] },
  "whisper": {
    "ggmlType": "base",        // tiny | base | small | medium | large-v3 | large-v3-turbo
    "language": "pt",          // "auto" para detectar
    "chunkSeconds": 5,         // tamanho do bloco enviado ao Whisper
    "modelDirectory": "",      // vazio = LocalFolder\models do pacote
    "modelFilePath": "",       // caminho explícito de um .bin (ignora ggmlType)
    "autoDownloadModel": true  // baixa do Hugging Face se faltar
  }
}
```

> **Restauração de pacotes:** o `nuget.config` local força o nuget.org (evita o
> feed privado que retornava `402` para `Whisper.net.Runtime`).

## Pré-requisitos do Windows (fora do código)

**Todos os motores** exigem acesso ao microfone:

- **Configurações → Privacidade e segurança → Microfone**
  → **Acesso ao microfone = ATIVADO** e **Permitir apps da área de trabalho = ATIVADO**.

**Apenas o motor "Windows — ditado on-line"** exige, adicionalmente:

- **Configurações → Privacidade e segurança → Fala** → **Reconhecimento de fala
  on-line = ATIVADO** (se desativado, falha com HRESULT `0x80045509`).
- Um **pacote de idioma de fala** instalado (ex.: pt-BR). O app usa `pt-BR` quando
  disponível e recai para o idioma de fala padrão do sistema.

> Os motores **Windows offline** e **Whisper** **não** dependem do toggle de fala
> on-line nem de internet (salvo o download único do modelo Whisper).

## Como executar

**Visual Studio 2022** (recomendado): abra `PocStt.csproj`, selecione a configuração
`Debug | x64` e pressione **F5** (implanta e executa o pacote).

**CLI** (compilação):

```powershell
dotnet build .\PocStt.csproj -c Debug -p:Platform=x64 -r win-x64
```

> A execução requer implantação do pacote MSIX; pelo VS isso é automático (F5).

## Estrutura

| Arquivo | Papel |
|---|---|
| `Package.appxmanifest` | Capabilities (`runFullTrust`, `microphone`, `internetClient`). |
| `config.json` | Configuração externa (motor padrão, idioma, comandos, opções Whisper). |
| `MainWindow.xaml(.cs)` | UI + orquestração via `ISpeechToTextEngine` e despacho à UI thread. |
| `Configuration/AppSettings.cs` | Modelo de configuração e loader do `config.json`. |
| `Speech/ISpeechToTextEngine.cs` | Contrato comum dos motores (eventos parcial/final/status/erro). |
| `Speech/WindowsSpeechEngineBase.cs` | Base do `SpeechRecognizer`: sessão contínua, auto-restart, teardown. |
| `Speech/WindowsDictationEngine.cs` | Motor on-line (ditado/fala livre). |
| `Speech/WindowsOfflineEngine.cs` | Motor offline (lista de comandos). |
| `Speech/WhisperEngine.cs` | Motor Whisper local (NAudio + Whisper.net + download do modelo). |
| `Speech/SttErrors.cs` | Mapeamento de HRESULTs e checagem de microfone. |
| `App.xaml(.cs)` | Bootstrap da aplicação. |
