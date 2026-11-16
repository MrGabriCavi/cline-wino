# HANDOVER: ClineWin

Passaggio di consegne per un agente AI che riprende il lavoro. Il documento descrive lo stato al 16 novembre 2026.
Leggilo tutto prima di modificare codice. Per l'elenco puntuale delle funzioni rispetto all'originale vedi [CONFRONTO.md](CONFRONTO.md).

---

## 1. Obiettivo e vincoli

**Cosa è ClineWin.** È la ricostruzione **nativa per Windows** dell'app di esempio **Cline Desktop**, che si trova nel repository di Cline in `apps/examples/desktop-app` (sulla macchina di sviluppo è nella cartella `cline` accanto a questo progetto). L'originale è fatto con Tauri, un sidecar Bun e una webview Next.js.

**Vincoli posti dall'utente**, da rispettare sempre:
- **Solo componenti nativi Windows** (WinUI 3): niente React, webview o simili.
- **Colori e immagini** vengono dal repository originale.
- **Barra dei menu classica di Windows**, che nell'originale non c'è.
- **Interfaccia in italiano**, compresi i commenti nel codice.
- **Niente sidecar.** L'utente ha chiesto di "rifare anche il backend", quindi il backend è in C# nell'app. Non reintrodurre Bun/Node né il WebSocket `ws://127.0.0.1:3126/transport`.
- **Niente commit senza richiesta esplicita.** `ClineWindows` è un repository git (branch `main`, remote `origin` = `git@github.com:MrGabriCavi/cline-wino.git`) creato il 16/11/2026, ma **senza commit**: tutti i file sono non tracciati. Non fare commit né push senza una richiesta esplicita.

**Lingua.** L'utente scrive in italiano. Rispondi in italiano, in modo conciso.

---

## 2. Ambiente e build

**Percorsi**
- **Progetto:** la cartella di questo repository (`ClineWindows`).
- **Soluzione e progetto:** `ClineWin.slnx`, `ClineWin.csproj`. RootNamespace `ClineWin`.
- **Repository originale, solo come riferimento:** `../cline/apps/examples/desktop-app` rispetto a questo progetto, con `webview/components/views/...` e `sidecar/*.ts`.

**Piattaforma**
- Windows App SDK 2.6.5 (WinUI 3), .NET 10, `net10.0-windows10.0.26100.0`, pacchetto MSIX.
- Il GUID dell'identità del pacchetto è invariato dal template.

**Pacchetti NuGet:** `Markdig` 1.6.0, `Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`, `Microsoft.Windows.SDK.BuildTools.WinApp`.

**Compilare.** Chiudi sempre prima l'app, altrimenti la DLL resta bloccata:
```powershell
Get-Process ClineWin -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build -p:Platform=x64 -nodeReuse:false -v q
```
Lo stato attuale è **0 avvisi, 0 errori**. Mantienilo così.

**Avviare.** Passa dalla WinApp CLI, che registra l'identità del pacchetto:
```powershell
dotnet run -p:Platform=x64 --no-build -p:WinAppRunDevTools=off
```

**Dove l'app salva i dati.** La cartella del pacchetto è `%LOCALAPPDATA%\Packages\b7c923f4-8805-4b74-a626-b19ebefd6179_<hash dell'editore>\`. Il nome del pacchetto viene da `Package.appxmanifest`; il suffisso cambia da macchina a macchina.
- `LocalState\sessions\*.json`: le sessioni di chat.
- `LocalState\schedules.json`: le attività pianificate.
- `Settings\settings.dat`: le preferenze (`ApplicationData.LocalSettings`).
- **Chiavi API:** stanno in Gestione credenziali di Windows (`PasswordVault`), risorsa `ClineWin`, utente = nome del provider. **Non leggerle e non stamparle mai.**

**Dati dell'utente da proteggere**
- In `PasswordVault` c'è una **chiave OpenRouter reale** dell'utente. Usala solo per prove brevi ed economiche (modello `amazon/nova-lite-v1`) e di' sempre all'utente quante richieste hai fatto.
- Le sessioni già presenti in `LocalState\sessions` sono dell'utente: non toccarle. Una è già stata danneggiata una volta da un bug e poi ripristinata. Per le prove crea sessioni a parte e cancellale alla fine.
- Le preferenze attuali sono: provider OpenRouter, modello Nova Lite, approvazione automatica solo per la lettura. Se le cambi per un test, ripristinale.

---

## 3. Architettura

```
App.xaml(.cs)            Tema scuro, risorse Themes/ClineTheme.xaml, gestore delle eccezioni → %TEMP%\clinewin-crash.txt
MainWindow.xaml(.cs)     Barra del titolo, menu, barra laterale (navigazione, impostazioni, cronologia), Frame RootFrame
Routes.cs                Tag → pagina (session, schedule, customize, settings/*)
Themes/ClineTheme.xaml   Palette @cline/ui convertita da OKLCH, pennelli Cline*, stili
Controls/                ClineLogo, DiffView (diff a righe), ToolCallView (riquadro di uno strumento + approvazione)
Pages/                   Una pagina per vista; WelcomePage = sessione/chat (divisa in 3 file parziali)
Services/                Backend nativo (vedi sotto)
```

### Backend (`Services/`)

**Chat e provider**
- `ChatClient.cs`: client per qualsiasi API compatibile con OpenAI.
  - Streaming SSE di `chat/completions`, ricostruzione delle `tool_calls` dai frammenti, `GET models` con `context_length`.
  - Messaggi multimodali: gli allegati diventano `<file>` nel testo e le immagini parti `image_url`.
  - Il JSON è scritto e letto a mano (`Utf8JsonWriter`/`JsonDocument`) per essere compatibile con il trimming.
  - Contiene anche i tipi `ChatMessage` (Role, Content, ToolCalls, ToolCallId, Attachments), `ToolCall`, `MessageAttachment`, `ChatTurn`, `ModelInfo` e `ProviderException`.
- `ProviderCatalog.cs`: 10 provider predefiniti più quelli personalizzati.
  - Con chiave: OpenRouter, Anthropic, OpenAI, Google Gemini, DeepSeek, Groq, Mistral, xAI.
  - Locali: Ollama `localhost:11434/v1`, LM Studio `localhost:1234/v1`.
- `CustomProviders.cs`: provider aggiunti dall'utente (nome, URL base, URL dei modelli facoltativo), salvati come JSON nella preferenza `customProviders`, con validazione.
- `CredentialStore.cs`: chiavi API in `PasswordVault`.

**Agente**
- `AgentTools.cs`: gli strumenti dell'agente, confinati nella cartella del progetto.
  - Lettura: `read_file` (senza numeri di riga, apposta), `list_files`, `search_files`.
  - Modifica: `write_to_file`, `replace_in_file`.
  - Comandi: `execute_command` (PowerShell, timeout di 2 minuti).
  - `PlanMode` offre solo la lettura e rifiuta il resto.
  - `Describe(call, live)` produce un `ToolPreview` con categoria (Read/Edit/Command) e `FileDiff` reale.
  - Contiene anche `SystemPrompt` e le istruzioni Piano/Agisci.
- `AutoApproval.cs`: approvazione automatica per categoria (preferenze `autoApprove.read|edit|command`). Di default solo la lettura.
- `LineDiff.cs`: diff a righe (LCS, prefisso e suffisso comuni, 3 righe di contesto, salti "N righe invariate").

**Sessioni e pianificazione**
- `SessionStore.cs`: una sessione per file JSON (`ChatSession`: titolo, provider, modello, progetto, modalità, messaggi). Evento `Changed`.
- `ScheduledTasks.cs`: attività pianificate (una volta / ogni giorno / giorni scelti), calcolo della prossima esecuzione, `schedules.json`.
- `Scheduler.cs`: timer ogni 30 secondi mentre l'app è aperta; recupera le esecuzioni saltate.
  - Ciclo dell'agente senza interfaccia: le azioni non coperte dall'approvazione automatica non vengono eseguite (`NotApprovedResult`).
  - Ogni esecuzione è una sessione "⏱ nome".

**Altro**
- `GitService.cs`: usa il `git` installato. Status porcelain, testi per il diff, add/restore/discard/commit, branch/switch.
- `MarkdownRenderer.cs`: Markdig trasformato in controlli WinUI (RichTextBlock, codice con "Copia", liste, tabelle, citazioni). I tag `<thinking>` diventano citazione.
- `FileIndex.cs`: indice dei file del progetto per `@`, cache di 30 secondi.
- `AppPreferences.cs`, `AppInfo.cs`, `ClineAuthService.cs` (login **dimostrativo**).

### WelcomePage (sessione)

La pagina ha `NavigationCacheMode=Required`, quindi c'è una sola istanza. È divisa in tre file.

**`WelcomePage.xaml.cs`**
- Progetto e branch, provider e modello, modalità Piano/Agisci, approvazione automatica.
- **Ciclo dell'agente** (`SendMessageAsync` → `StreamTurnAsync`): al massimo 25 passi, approvazioni, interruzione, `CompleteDanglingToolCalls`.
- Salvataggio e riapertura delle sessioni (`OpenSessionAsync`, con riquadri degli strumenti `live:false`).

**`WelcomePage.Composer.cs`**
- `@` per i file e `/` per i comandi (`/nuova`, `/piano`, `/agisci`).
- Allegati: graffetta, incolla, trascina. Le immagini degli appunti vengono convertite in PNG.
- Coda dei messaggi e stima dei token.

**`WelcomePage.Git.cs`**: pannello delle modifiche e menu dei branch.

---

## 4. Funzioni complete

Tutte quelle che seguono sono state provate nell'app.

**Chat e agente**
- Chat in streaming, multi-turno, interruzione, Markdown nativo.
- 10 provider, provider personalizzati, BYOK con chiavi in `PasswordVault`, elenco modelli dal provider.
- Agente su progetto con approvazioni (Approva / Approva sempre / Rifiuta), approvazione automatica per categoria, modalità Piano/Agisci con pulsante "Passa ad Agisci", diff delle modifiche.

**Sessioni e pianificazione**
- Cronologia delle sessioni nella barra laterale: riapertura, eliminazione, selezione anche da tastiera o UIA.
- Pianificazione: modelli pronti, esegui ora, recupero all'avvio, sessione per esecuzione.

**Composer e git**
- Composer: `@` file, `/` comandi, immagini e file allegati, coda, stima dei token.
- Pannello git: stage/unstage/annulla/commit, diff, cambio e creazione di branch.

**Impostazioni**
- Generali (salvate, non applicate), Locale (stato reale), Scorciatoie (salvate, non applicate).
- Importa (rileva soltanto), Remoto (solo dati), Debug (esportazione reale), Informazioni.

---

## 5. Cosa manca, in ordine di priorità suggerito

Dettagli in [CONFRONTO.md](CONFRONTO.md).

1. **Sessioni:** ricerca (`Ctrl+P` e icona lente) e rinomina. Sono rimasti pulsanti senza effetto nella barra laterale (raggruppa, filtra).
2. **Git:** push e pull, cronologia dei commit, worktree (l'interruttore c'è ma non fa nulla).
3. **Personalizza** (oggi `PlaceholderPage`): regole del progetto (`.clinerules`), server MCP, skill, hook, marketplace.
4. **Preferenze da applicare davvero:** colore d'accento, dimensione del testo, icona, scorciatoie ai menu.
5. **Pulsanti senza effetto:** Terminale, selettore del ragionamento (Basso/Medio/Alto).
6. **Agente:** checkpoint e ripristino, terminale integrato, ricerca web, comandi "sicuri" per l'approvazione automatica, modello separato per Piano e Agisci.
7. **Provider:** OAuth (Codex), CLI locali (Claude Code), Bedrock/Vertex. Download dei modelli locali (Ollama pull).
8. **Account Cline vero, onboarding, cloud, Voce, Importa vero, Remoto (SSH) vero.**
9. **Pianificazione ad app chiusa** (Utilità di pianificazione di Windows) e notifiche a fine esecuzione.

---

## 6. Problemi noti e trappole già incontrate

**XAML e WinUI**
- **Commenti XAML:** niente `--` dentro i commenti (errore WMC9999).
- **`ThemeDictionaries`** in `ClineTheme.xaml` deve restare **alla fine** del dizionario (errore WMC0035).
- **WMC9999 "Riferimento a un oggetto non impostato"** con un errore C# nella pagina: di solito sparisce dopo aver corretto il C#. Altrimenti cancella `obj\x64\Debug\...\Pages`.
- **`KeyboardAccelerator` con il tasto virtuale 188** (virgola) fa crashare l'app all'avvio.
- **`Border` è sealed**: per i controlli composti eredita da `UserControl` (vedi `ToolCallView`, `DiffView`).
- **Una sola `ContentDialog` aperta alla volta.** Aprirne una seconda chiude l'app (0xc000027b, senza file di crash). Usa una guardia `_dialogOpen`, come in `ByokPage` e `SchedulePage`.
- **`ContentDialog` taglia i moduli larghi:** imposta `dialog.Resources["ContentDialogMaxWidth"]`.
- **Collection expression verso `IReadOnlyList<T>`** dà l'avviso CsWinRT1032: usa un cast ad array (`(T[])[...]`) o `.ToArray()`.
- **Nomi:** `Template` in una `Page` nasconde `Control.Template`, quindi non usarlo come nome di tipo annidato.

**Sessioni e liste**
- **`ListView` con `SingleSelectionFollowsFocus`** (default true): ricaricando l'elenco delle sessioni la selezione seguiva il focus e apriva un'altra sessione. Ora è `False`, con la guardia `_updatingSessionList`. **Non rimuoverli.**
- **Progetto e sessioni:** aprendo una sessione senza progetto bisogna chiamare `ClearProject()`, altrimenti il progetto precedente finisce salvato nella sessione sbagliata.

**Comportamento dei modelli**
- **Immagini dagli appunti:** sono BMP. Senza conversione in PNG il provider risponde 400.
- **Nova Lite** (il modello economico usato per le prove):
  - ricopiava i numeri di riga di `read_file`, quindi la lettura restituisce il testo puro;
  - dopo un piano in modalità Piano non agisce da solo;
  - a volte risponde in inglese o ripropone un'azione rifiutata.

  Sono limiti del modello, non bug dell'app.

---

## 7. Come provare l'interfaccia (automazione)

**Script.** Erano nella cartella temporanea dell'agente precedente e non sono nel repository: ricreali se servono. Sono PowerShell con UI Automation (`System.Windows.Automation`).
- `shot.ps1 <out.png>`: screenshot della finestra (PrintWindow). Non cattura menu e flyout, che sono finestre popup a parte.
- `click.ps1 <Nome>`: Invoke o Select via UIA per nome (salta le voci del MenuBar).
- `setvalue.ps1 <AutomationId> <testo>`: imposta il testo di un campo.

**Consigli**
- **Sessioni di prova:** crea un JSON in `LocalState\sessions` con `projectPath` verso una cartella di prova (per esempio una cartella temporanea con qualche file, o un repository git creato apposta per le prove). Selezionala con `click.ps1 "<titolo>"` e **cancellala alla fine**.
- **Menu e flyout:** cerca gli elementi tra tutti quelli del processo (`ProcessIdProperty` su `RootElement`), non sotto la finestra. Per i `DropDownButton` del composer i clic del mouse sono inaffidabili: usa la tastiera (`SetFocus` più `WScript.Shell.SendKeys`: spazio, Home, Invio).
- **Tastiera nel composer:** `SendKeys` sulla `PromptBox` funziona bene per provare `@`, `/` e la coda.
- **Pulsanti senza nome:** un pulsante senza `AutomationProperties.Name` non si trova con UIA. Aggiungi sempre il nome, serve anche agli screen reader.

---

## 8. Convenzioni del codice

- **Commenti e testi in italiano.** I commenti spiegano il *perché*. Messaggi d'errore chiari per l'utente.
- **Accessibilità:** `AutomationProperties.Name` su tutti i pulsanti con solo icona.
- **Colori:** solo pennelli del tema (`ClineText1Brush`, `ClineCardBrush`, `ClinePrimaryBrush`, `ClineSuccessBrush`, `ClineErrorTextBrush`…). Niente colori fissi, tranne gli sfondi dei diff.
- **Trimming:** JSON con source generator (`JsonSerializerContext`) o scritto a mano. Niente serializzazione per riflessione.
- **Azioni distruttive** (eliminare, annullare modifiche, rimuovere un provider): sempre con una `ContentDialog` di conferma.
- **Documenti da aggiornare:** a ogni funzione aggiorna [REPORT.md](REPORT.md) (stato) e [CONFRONTO.md](CONFRONTO.md) (confronto con l'originale).

---

## 9. Cronologia del lavoro

1. Analisi delle cartelle, `.gitignore`, rinomina del progetto da AppTry a ClineWin.
2. Analisi di Cline Desktop e del sidecar.
3. Interfaccia nativa: finestra, barra dei menu, barra laterale, home con il robot, tema da `@cline/ui`.
4. Impostazioni con login dimostrativo, poi tutte le pagine delle impostazioni.
5. Abbandono del sidecar e backend in C#: chat OpenRouter in streaming, chiavi in `PasswordVault`.
6. Markdown nativo e cronologia delle sessioni.
7. Strumenti dell'agente con approvazioni, poi approvazione automatica, poi Piano/Agisci, con le correzioni ai bug delle sessioni.
8. Altri provider, poi provider personalizzati, poi diff leggibili.
9. Pianificazione.
10. Confronto con l'originale (`CONFRONTO.md`).
11. Composer (`@`, `/`, allegati, coda, token), poi pannello git.
