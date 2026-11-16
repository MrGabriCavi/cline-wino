# ClineWin: stato del lavoro

Versione nativa per Windows (WinUI 3, .NET 10) dell'interfaccia di Cline Desktop.
Colori, icone e testi vengono dall'app originale (`cline/apps/examples/desktop-app` e `@cline/ui`).
In più c'è la barra dei menu classica di Windows. L'interfaccia è in italiano.

Compila senza errori né avvisi. Nessun commit.

## Cosa c'è

| Area | Stato |
|---|---|
| Finestra, barra del titolo, menu (File, Modifica, Visualizza, Sessione, Aiuto) | Fatto |
| Barra laterale con navigazione, sezione Impostazioni con gruppo Provider | Fatto |
| Home (robot, composer, scelta progetto con branch git) | Fatto |
| Chat in streaming (più turni, interruzione, scelta tra i modelli reali) | Fatto, backend C# senza sidecar |
| Provider: OpenRouter, Anthropic, OpenAI, Google Gemini, DeepSeek, Groq, Mistral, xAI, Ollama, LM Studio (tutti via API compatibile OpenAI) | Fatto; provato solo OpenRouter (unica chiave disponibile) |
| Agente sul progetto aperto: leggere, elencare e cercare file; scrivere e modificare file; comandi PowerShell | Fatto; max 25 passi per richiesta |
| Approvazione automatica per tipo (lettura, modifiche, comandi): menu nel composer e "Approva sempre" nel riquadro | Fatto; di default solo la lettura è automatica |
| Diff delle modifiche (numeri di riga, contesto, righe aggiunte/tolte colorate, conteggi +N −N) | Fatto |
| Modalità Piano/Agisci (selettore nel composer, salvata nella sessione; in Piano solo strumenti di lettura e pulsante "Passa ad Agisci") | Fatto |
| Pannello git (modifiche preparate e non, diff, stage/unstage, annulla, commit) e menu dei branch (cambio, creazione) | Fatto; usa il git installato |
| Composer: `@` per i file, `/` per i comandi, allegati e immagini (graffetta, incolla, trascina), messaggi in coda, stima dei token | Fatto |
| Markdown nelle risposte (Markdig + controlli nativi: titoli, elenchi, codice con Copia, tabelle, link, citazioni) | Fatto |
| Cronologia sessioni (JSON in LocalState\sessions, elenco nella barra laterale, riapertura, eliminazione) | Fatto |
| Impostazioni: Generali | Fatto; le scelte vengono salvate ma non applicate |
| Account Cline (Panoramica, Utilizzo, Fatturazione, accesso con codice) | Interfaccia fatta, accesso dimostrativo |
| Modelli locali (Ollama, LM Studio) | Stato reale, download non collegato |
| BYOK | I 10 provider collegati; chiavi in Gestione credenziali di Windows; elenco modelli dal provider |
| Provider personalizzati (qualsiasi servizio compatibile OpenAI: nome, URL, URL modelli e chiave facoltativi) | Fatto; aggiunta, validazione, elenco modelli e rimozione provati |
| Voce | Stato vuoto come nell'originale |
| Scorciatoie da tastiera | Registrazione e salvataggio; non applicate ai menu |
| Importa | Ricerca reale (trovate 27 sessioni Claude Code e 10 Codex); importazione non collegata |
| Ambienti remoti (host SSH) | Elenco e modulo salvati in locale; test di connessione non collegato |
| Debug | Esportazione della diagnostica e apertura della cartella dei log |
| Informazioni | Versione e link; controllo aggiornamenti non disponibile |
| Pianificazione (attività una volta / ogni giorno / giorni scelti, modelli pronti, esegui ora, recupero delle esecuzioni saltate, ogni esecuzione è una sessione) | Fatto; parte solo ad app aperta; senza utente esegue solo le azioni ad approvazione automatica |
| Personalizza | Segnaposto |

## Dati reali e dati dimostrativi

- **Reali:** stato di Ollama e LM Studio, sessioni da importare, log di Cline, notifiche di Windows, branch git del progetto scelto.
- **Dimostrativi:** accesso all'account (codice `DEMO-CODE`, non completa mai).
- **Salvati ma non applicati:** colore d'accento, dimensione del testo, icona dell'app, scorciatoie.

## Non ancora verificato

Le pagine sono state aperte e fotografate tutte. Non sono state provate a mano:

- registrazione di una scorciatoia;
- aggiunta, modifica ed eliminazione di un host SSH;
- dettaglio di un provider BYOK;
- esportazione della diagnostica;
- voci dei menu, scelta del progetto, chip del branch.

## Prossimi passi possibili

1. **Proseguire il backend nativo in C#** (il sidecar non si usa):
   Bedrock/Vertex (autenticazione propria); per l'agente: modello diverso per Piano e Agisci, checkpoint per annullare le modifiche.
2. **Pagina Personalizza**, oggi segnaposto; per la Pianificazione: esecuzione anche ad app chiusa (Utilità di pianificazione di Windows) e notifiche a fine esecuzione.
3. **Applicare le preferenze già salvate:** accento, dimensione del testo, scorciatoie nei menu.
