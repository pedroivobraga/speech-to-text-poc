# Privacy and Speech Recognition — Engine Analysis

> Companion document for the PoC. Explains **where the audio goes**, what each engine
> requires in terms of privacy, which alternatives exist without the online service, and
> what you gain/lose along each path.

---

## 1. Overview of the 3 engines

| Engine | Technology | Audio leaves the device? | Internet | Free-form |
|---|---|:--:|:--:|:--:|
| **Windows — online dictation** | `SpeechRecognitionTopicConstraint` (dictation) | **Yes** (Microsoft cloud) | Yes | Yes |
| **Windows — offline** | `SpeechRecognitionListConstraint` (commands) | **No** | No | No (fixed list) |
| **Whisper — local** | Whisper.net + NAudio | **No** | Only to download the model (first run) | Yes |

The key privacy rule: **only online dictation sends your voice off the machine**. The other
two process everything locally.

---

## 2. The online dictation engine (the PoC default)

`SpeechRecognitionTopicConstraint` with `SpeechRecognitionScenario.Dictation` is a
**predefined free-form grammar**: you declare no vocabulary, it transcribes anything. In
return, it relies on **Microsoft's online speech recognition service** — the audio is sent
to the cloud, processed, and the text is returned.

### Audio flow

```
[Microphone] --> [App] --> (network) --> [Microsoft speech service] --> text --> [App]
```

### What is at stake

- **Your voice leaves the device** and travels to Microsoft servers while you dictate.
- A **separate** setting ("contribute voice clips" / "help improve speech recognition"), if
  enabled, lets Microsoft **retain samples** to train/improve services.
- **Compliance (LGPD/GDPR):** for sensitive data (health, legal, financial), this typically
  requires a legal basis, notice to the data subject, and is often prohibited by internal
  policy — because the data leaves the controlled environment.
- **Network dependency:** latency and unavailability when offline.

### Windows prerequisites (outside the code)

1. **Settings → Privacy & security → Speech → "Online speech recognition" = ON.** If it is
   off, grammar compilation fails with `HRESULT 0x80045509`.
2. **Settings → Privacy & security → Microphone → access enabled** (including "let desktop
   apps access your microphone").
3. The **Speech** language pack installed (e.g., pt-BR / en-US).

> Honest note: the exact routing (cloud vs. on-device) can vary by Windows version and
> language pack, but the privacy gate and the network requirement are the rule for the
> *dictation* scenario. Treat it as **cloud-dependent**.

---

## 3. Alternatives without the online service

### 3.1. The same Windows API, but with other constraints

`Windows.Media.SpeechRecognition` runs **100% on-device** if you switch the constraint type
— which is exactly what the **Windows offline** engine does:

| Constraint | How it works | On-device |
|---|---|:--:|
| `SpeechRecognitionTopicConstraint` (dictation) | Free-form | ❌ (cloud) |
| `SpeechRecognitionListConstraint` | Fixed list of words/phrases | ✅ |
| `SpeechRecognitionGrammarFileConstraint` | SRGS grammar (XML, with rules/slots) | ✅ |

The latter two use the **local Windows speech engine** (from the language packs). They need
no internet, no "Online speech recognition" toggle, and **send no audio anywhere**.

```csharp
// Offline example — fixed commands (the PoC's "Windows offline" engine):
var list = new[] { "start", "stop", "save", "cancel", "next", "previous" };
recognizer.Constraints.Add(new SpeechRecognitionListConstraint(list, "commands"));
await recognizer.CompileConstraintsAsync(); // compiles locally, no network
```

### 3.2. Free-form offline → third-party (local) engines

The native API does **not** offer free-form offline dictation. For that, local engines:

- **Whisper** (via `Whisper.net` / `whisper.cpp`) — free-form, high quality, on-device. It
  is the closest substitute to dictation in quality. **This is the PoC's "Whisper" engine.**
- **Vosk** — lightweight, many languages, real-time streaming, on-device.

> Whisper only needs internet **once**, to download the `ggml` model. The recognition itself
> is fully local.

### 3.3. Cloud, but under your control

- **Azure AI Speech SDK** — still cloud, but **you** control the region, contract, and data
  retention. It does not solve "offline", it solves "data under my contract".

---

## 4. What you lose going offline (native API: list/grammar)

| You gain | You lose |
|---|---|
| Audio never leaves the device (privacy/compliance) | **Free-form speech** — only recognizes what's in the list/grammar |
| Works without internet, low latency | Open vocabulary and sentence context |
| No privacy toggle / no cloud dependency | Automatic punctuation and capitalization |
| Deterministic recognition (great for commands) | Accent/noise robustness and always-updated models |
| No service cost | Some languages only exist on the online path |

With **Whisper** you regain free-form offline speech, at the cost of **CPU/RAM** (larger
models = more accuracy and more usage) and higher latency (it processes in blocks, not
native streaming).

---

## 5. Recommendation by scenario

| Scenario | Recommended engine |
|---|---|
| Voice commands / short, predictable dictation | **Windows offline** (`ListConstraint`/`GrammarFileConstraint`) |
| Free-form dictation **with** privacy / no internet | **Whisper local** |
| Free-form dictation, privacy **not** a blocker | **Windows online dictation** |
| Cloud acceptable, but with data governance | **Azure AI Speech** (out of scope for this PoC) |

---

## 6. 30-second summary

- **Online dictation = your voice goes to Microsoft's cloud.** Requires the privacy toggle,
  the microphone, and (in practice) internet.
- **Want full privacy?** Use **Windows offline** (commands) or **Whisper** (free-form) —
  both process everything on the device.
- **The native offline trade-off** is giving up free-form speech; **Whisper** brings
  free-form offline back in exchange for more CPU/RAM and latency.

---

_Cross-reference: see `README.md` (configuration and how to run) and `config.json` (engine
selection and options). Portuguese version: [`PRIVACIDADE.md`](PRIVACIDADE.md)._
