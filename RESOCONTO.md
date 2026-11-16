# ClineWin: resoconto

## Cos'è

ClineWin è una versione di **Cline Desktop** scritta da zero come app nativa per Windows (WinUI 3, .NET 10), con l'interfaccia in italiano.

L'originale è fatto di tre pezzi: un'interfaccia web, un "sidecar" Bun e un guscio Tauri. ClineWin invece è un solo programma Windows con il backend in C#: non serve installare né avviare altro. Rispetto all'originale ha in più la barra dei menu classica di Windows.

## Cosa funziona già

- **Chat con l'IA:** le risposte arrivano mentre vengono scritte e sono formattate (titoli, elenchi, codice con "Copia", tabelle).
- **10 provider pronti:** OpenRouter, Anthropic, OpenAI, Gemini, DeepSeek, Groq, Mistral, xAI, Ollama e LM Studio. Si possono aggiungere servizi propri compatibili con OpenAI. Le chiavi API sono salvate cifrate in Windows.
- **Agente sul progetto:** con una cartella aperta, Cline legge, cerca e modifica i file ed esegue comandi.
  - Ogni modifica si vede come differenza riga per riga, con i colori.
  - Le azioni importanti chiedono "Approva / Rifiuta". Per tipo di azione si può scegliere di approvare in automatico.
  - Modalità **Piano** (solo propone) e **Agisci** (esegue).
- **Casella dei messaggi:**
  - `@` per citare un file del progetto, `/` per i comandi;
  - immagini e file allegati con la graffetta, incollando o trascinando;
  - messaggi in coda mentre Cline lavora;
  - stima dei token usati.
- **Cronologia:** le conversazioni sono salvate e si riaprono dalla barra laterale.
- **Pannello git:** file cambiati, differenze, "prepara", "annulla" e commit. Cambio e creazione di branch.
- **Pianificazione:** attività che l'agente esegue da solo a un orario stabilito, a partire da modelli pronti.
- **Impostazioni:** tutte le pagine dell'originale, alcune ancora solo grafiche (vedi sotto).

## Cosa manca ancora

- **Personalizza:** regole del progetto, server MCP, plugin e marketplace.
- **Sessioni:** ricerca e rinomina. Alcuni pulsanti della barra laterale non fanno ancora nulla.
- **Git:** push e pull, cronologia dei commit, worktree.
- **Funzioni legate ai servizi Cline:** account vero, cloud, primo avvio guidato.
- **Altro:** voce, terminale integrato, checkpoint per annullare il lavoro dell'agente.
- **Impostazioni salvate ma non ancora applicate:** colore, dimensione del testo, scorciatoie.

L'elenco completo, voce per voce, è in [CONFRONTO.md](CONFRONTO.md).

## Note

- Ogni funzione è stata provata nell'app. Per le prove con l'IA è stato usato un modello economico (Nova Lite) tramite la chiave OpenRouter, con poche richieste ogni volta.
- Nessun commit è stato fatto. La cartella è un repository git collegato a GitHub (`MrGabriCavi/cline-wino`), ancora senza commit: tutti i file sono da aggiungere.
- Documenti nel progetto:
  - [HANDOVER.md](HANDOVER.md): dettagli tecnici per chi continua il lavoro, persona o agente IA;
  - [CONFRONTO.md](CONFRONTO.md): confronto con l'originale;
  - [REPORT.md](REPORT.md): stato sintetico per area.
