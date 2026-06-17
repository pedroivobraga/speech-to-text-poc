# Privacidade e Reconhecimento de Voz — Análise dos Motores

> Documento de apoio do PoC. Explica **para onde vai o áudio**, o que cada motor
> exige em termos de privacidade, quais alternativas existem sem o serviço on-line
> e o que se ganha/perde em cada caminho.

---

## 1. Visão geral dos 3 motores

| Motor | Tecnologia | Áudio sai do dispositivo? | Internet | Fala livre |
|---|---|:--:|:--:|:--:|
| **Windows — ditado on-line** | `SpeechRecognitionTopicConstraint` (dictation) | **Sim** (nuvem Microsoft) | Sim | Sim |
| **Windows — offline** | `SpeechRecognitionListConstraint` (comandos) | **Não** | Não | Não (lista fixa) |
| **Whisper — local** | Whisper.net + NAudio | **Não** | Só p/ baixar o modelo (1ª vez) | Sim |

A regra-chave de privacidade: **só o ditado on-line envia sua voz para fora da
máquina**. Os outros dois processam tudo localmente.

---

## 2. O motor de ditado on-line (o padrão da PoC)

O `SpeechRecognitionTopicConstraint` com `SpeechRecognitionScenario.Dictation` é uma
**gramática predefinida de fala livre**: você não declara vocabulário, ele transcreve
qualquer coisa. Em troca, depende do **serviço de reconhecimento de fala on-line da
Microsoft** — o áudio é enviado à nuvem, processado e o texto retorna.

### Fluxo do áudio

```
[Microfone] --> [App] --> (rede) --> [Serviço de fala da Microsoft] --> texto --> [App]
```

### O que está em jogo

- **A voz sai do dispositivo** e trafega até servidores da Microsoft enquanto você dita.
- Uma configuração **separada** ("contribuir com clipes de voz" / "ajudar a melhorar o
  reconhecimento de fala"), se ligada, permite que a Microsoft **retenha amostras** para
  treinar/melhorar serviços.
- **Compliance (LGPD/GDPR):** para dados sensíveis (saúde, jurídico, financeiro), isso
  normalmente exige base legal, aviso ao titular e, em muitos casos, é vetado por política
  interna — porque o dado deixa o ambiente controlado.
- **Dependência de rede:** latência e indisponibilidade quando offline.

### Pré-requisitos no Windows (fora do código)

1. **Configurações → Privacidade e segurança → Fala → "Reconhecimento de fala on-line" =
   ATIVADO.** Se desligado, a compilação da gramática falha com `HRESULT 0x80045509`.
2. **Configurações → Privacidade e segurança → Microfone → acesso ativado** (inclusive
   "permitir apps da área de trabalho").
3. Pacote de **fala** do idioma instalado (ex.: pt-BR).

> Observação honesta: o roteamento exato (nuvem vs. on-device) pode variar por versão do
> Windows e por pacote de idioma, mas o gatilho de privacidade e a necessidade de rede são
> a regra para o cenário de *dictation*. Trate-o como **dependente de nuvem**.

---

## 3. Alternativas sem o serviço on-line

### 3.1. A própria API do Windows, mas com outras *constraints*

A `Windows.Media.SpeechRecognition` roda **100% on-device** se você trocar o tipo de
constraint — é exatamente o que o motor **Windows offline** faz:

| Constraint | Como funciona | On-device |
|---|---|:--:|
| `SpeechRecognitionTopicConstraint` (ditado) | Fala livre | ❌ (nuvem) |
| `SpeechRecognitionListConstraint` | Lista fixa de palavras/frases | ✅ |
| `SpeechRecognitionGrammarFileConstraint` | Gramática SRGS (XML, com regras/slots) | ✅ |

As duas últimas usam o **motor de fala local do Windows** (o dos pacotes de idioma). Não
precisam de internet, nem do toggle de "Reconhecimento de fala on-line", e **não enviam
áudio para lugar nenhum**.

```csharp
// Exemplo offline — comandos fixos (motor "Windows offline" da PoC):
var lista = new[] { "iniciar", "parar", "salvar", "cancelar", "próximo", "anterior" };
recognizer.Constraints.Add(new SpeechRecognitionListConstraint(lista, "comandos"));
await recognizer.CompileConstraintsAsync(); // compila local, sem rede
```

### 3.2. Fala livre offline → motores de terceiros (locais)

A API nativa **não** oferece ditado livre offline. Para isso, motores locais:

- **Whisper** (via `Whisper.net` / `whisper.cpp`) — fala livre, alta qualidade, on-device.
  É o substituto mais próximo do ditado em qualidade. **É o motor "Whisper" da PoC.**
- **Vosk** — leve, vários idiomas, streaming em tempo real, on-device.

> O Whisper só precisa de internet **uma vez**, para baixar o modelo `ggml`. O
> reconhecimento em si é totalmente local.

### 3.3. Nuvem, mas sob seu controle

- **Azure AI Speech SDK** — continua sendo nuvem, mas **você** controla região, contrato e
  retenção dos dados. Não resolve "offline", resolve "dados sob meu contrato".

---

## 4. O que se perde indo para offline (API nativa: list/grammar)

| Você ganha | Você perde |
|---|---|
| Áudio nunca sai do dispositivo (privacidade/compliance) | **Fala livre** — só reconhece o que está na lista/gramática |
| Funciona sem internet, baixa latência | Vocabulário aberto e contexto de frase |
| Sem toggle de privacidade / sem dependência de nuvem | Pontuação e capitalização automáticas |
| Reconhecimento determinístico (ótimo p/ comandos) | Robustez a sotaque/ruído e modelos sempre atualizados |
| Sem custo de serviço | Alguns idiomas só existem no caminho on-line |

Com **Whisper** você recupera a fala livre offline, ao custo de **CPU/RAM** (modelos maiores
= mais precisão e mais consumo) e de uma latência maior (processa em blocos, não streaming
nativo).

---

## 5. Recomendação por cenário

| Cenário | Motor recomendado |
|---|---|
| Comandos de voz / ditados curtos e previsíveis | **Windows offline** (`ListConstraint`/`GrammarFileConstraint`) |
| Ditado livre **com** privacidade / sem internet | **Whisper local** |
| Ditado livre e privacidade **não** é bloqueante | **Windows ditado on-line** |
| Nuvem aceitável, mas com governança de dados | **Azure AI Speech** (fora do escopo desta PoC) |

---

## 6. Resumo de 30 segundos

- **Ditado on-line = sua voz vai para a nuvem da Microsoft.** Exige toggle de privacidade,
  microfone e (na prática) internet.
- **Quer privacidade total?** Use **Windows offline** (comandos) ou **Whisper** (fala livre)
  — ambos processam tudo no dispositivo.
- **O trade-off do offline nativo** é abrir mão da fala livre; o **Whisper** devolve a fala
  livre offline em troca de mais CPU/RAM e latência.

---

_Referência cruzada: ver `README.md` (configuração e execução) e o arquivo `config.json`
(seleção de motor e opções). English version: [`PRIVACY.md`](PRIVACY.md)._
