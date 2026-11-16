# ClineWin e Cline Desktop: cosa c'è e cosa manca

Confronto con `cline/apps/examples/desktop-app`, fatto su due livelli.

- **Interfaccia:** le viste in `webview/components/views`, le sezioni delle impostazioni (`sections.ts`) e le schede di Personalizza.
- **Backend:** circa 150 comandi del sidecar (`sidecar/*.ts`). Ogni comando corrisponde a una funzione.

**Legenda:** ✅ fatto · 🟡 parziale o diverso · ❌ manca

In sintesi: il nucleo c'è (chat, agente, approvazioni, Piano/Agisci, provider, cronologia, pianificazione). Mancano soprattutto le estensioni (MCP, connettori, plugin, skill, regole, hook, marketplace), il pannello git/modifiche, le funzioni cloud dell'account Cline e alcune comodità del composer.

## Chat e composer

| Funzione dell'originale | ClineWin |
|---|---|
| Chat in streaming, più turni, interruzione | ✅ |
| Rendering Markdown, blocchi di codice con Copia | ✅ (manca l'evidenziazione della sintassi, `highlighted-code.tsx`) |
| Blocco del ragionamento del modello (`reasoning-block`) | 🟡 solo i tag `<thinking>`, mostrati come citazione. Il selettore Basso/Medio/Alto non ha effetto |
| Scelta di provider e modello | ✅ |
| `@` per menzionare file (`search_workspace_files`) | ✅ suggerimenti mentre scrivi; il contenuto del file viene inviato al modello |
| `/` per i comandi (`list_plugin_commands`) | 🟡 comandi propri: `/nuova`, `/piano`, `/agisci` (mancano quelli dei plugin) |
| Allegati e immagini (incolla, trascina, anteprima, lightbox) | ✅ graffetta, incolla, trascina, miniature (manca l'ingrandimento a schermo intero) |
| Input vocale e trascrizione (`transcribe_audio`, `voice-input-view`) | ❌ la pagina Voce è solo uno stato vuoto |
| Prompt in coda mentre l'agente lavora (`pending_prompts`, `steer_prompt`, `proceed_while_running`) | 🟡 coda con rimozione; manca la modifica in corsa (steer) |
| Domande dell'agente all'utente (`poll_ask_questions` / `respond_ask_question`) | ❌ |
| Indicatore dei token usati (`token-usage-ring`) e riepilogo del turno (`turn-receipt`) | 🟡 stima dei token, con la percentuale se il provider dichiara la memoria del modello; manca il riepilogo del turno |
| Indice della conversazione (`transcript-outline`) | ❌ |
| Artefatti pubblicati e immagini generate | ❌ |
| Verifica UX (`ux-verification-message`) | ❌ |

## Agente e strumenti

| Funzione dell'originale | ClineWin |
|---|---|
| Lettura, elenco e ricerca di file, scrittura e modifica, comandi | ✅ propri, in C# |
| Approvazione delle azioni (`tool-approval-panel`, `respond_tool_approval`) | ✅ Approva / Approva sempre / Rifiuta |
| Approvazione automatica per tipo | ✅ (manca l'elenco dei comandi considerati sicuri) |
| Modalità Piano/Agisci | ✅ (manca un modello separato per ciascuna modalità) |
| Diff delle modifiche | ✅ (manca l'evidenziazione delle parole cambiate dentro una riga) |
| Terminale integrato | ❌ il pulsante Terminale non fa nulla; i comandi girano in background |
| Abilitare o disabilitare singoli strumenti (`set_tool_disabled`) | ❌ |
| Ricerca web dell'agente (`web-search-provider-guidance`) | ❌ |
| Sotto-agenti e team (`list_session_agents`, `team_progress`) | ❌ |
| Checkpoint e ripristino (`restore-checkpoint`) | ❌ |

## Progetto e git

| Funzione dell'originale | ClineWin |
|---|---|
| Scelta della cartella e branch corrente | ✅ |
| Cambio di branch (`list_git_branches`, `checkout_git_branch`) | ✅ menu sul chip del branch: cambio e creazione |
| Worktree separato (`create_git_worktree`) | ❌ l'interruttore c'è ma non fa nulla |
| Pannello delle modifiche e source control (`changes-panel`, `stage`/`unstage`/`discard`, `run_source_control_action`) | ✅ file preparati e non, diff, stage/unstage (anche tutto insieme), annulla con conferma, commit; 🟡 mancano push/pull e i dettagli dei commit |
| Pannello dei file del progetto (`project-files-panel`, `read_project_file`) | ❌ |
| Pull request (`pull-request-menu`, `get_pull_request_status`) | ❌ |
| Apertura nell'editor (`open_file_in_editor`, `list_available_editors`) | ❌ |

## Sessioni

| Funzione dell'originale | ClineWin |
|---|---|
| Cronologia nella barra laterale, riapertura, eliminazione | ✅ |
| Rinominare una sessione (`update_chat_session_title`) | ❌ |
| Cerca sessioni (`search_sessions`, `Ctrl+P`) | ❌ l'icona della lente c'è ma non fa nulla |
| Raggruppa per progetto, ordina, filtra, pagine | ❌ i pulsanti ci sono ma non fanno nulla |
| Sessioni della CLI e scoperte (`list_cli_sessions`, `list_discovered_sessions`) | ❌ |
| Importazione da Claude Code, Codex, opencode (`import_sessions`) | 🟡 trova le sessioni ma non le importa |

## Impostazioni

| Sezione dell'originale | ClineWin |
|---|---|
| Generali | 🟡 le scelte vengono salvate ma non applicate: accento, dimensione del testo, icona. Notifiche: impostazioni e permessi reali, ma l'app non manda notifiche |
| Account Cline (accesso, utilizzo, fatturazione) | 🟡 interfaccia dimostrativa: l'accesso non si completa e i dati non sono reali |
| Modelli locali (Ollama, LM Studio) | 🟡 lo stato è reale; download, caricamento ed eliminazione dei modelli (`pull_local_model`, …) mancano |
| BYOK / Provider | ✅ 10 provider più quelli personalizzati. 🟡 mancano l'accesso OAuth (es. Codex), le CLI locali (Claude Code), Bedrock e Vertex, e la modifica dell'elenco dei modelli (`update_provider_models`) |
| Voce | ❌ |
| Scorciatoie | 🟡 si registrano e si salvano, ma non vengono applicate ai menu |
| Canali (nascosta anche nell'originale) | ❌ |
| Pianificazione | ✅ le attività partono solo ad app aperta; manca la notifica a fine esecuzione |
| Importa | 🟡 come sopra |
| Remoto (host SSH) | 🟡 elenco e modulo salvati; connessione, test ed esecuzione remota mancano |
| Debug | ✅ esportazione della diagnostica e cartella dei log |
| Informazioni | 🟡 aggiornamenti automatici (`set_auto_update_enabled`) e consenso alla telemetria (`set_telemetry_opt_out`) mancano |

## Personalizza e Marketplace

| Funzione dell'originale | ClineWin |
|---|---|
| Connettori e integrazioni (Composio, Cline) | ❌ |
| Strumenti, Plugin, Skill, Regole (`list_user_instruction_configs`), Hook | ❌ |
| Server MCP (`list_mcp_servers`, `upsert_mcp_server`, OAuth, ispezione) | ❌ |
| Marketplace (`install_marketplace_entry`) | ❌ |

Personalizza in ClineWin è ancora un segnaposto.

## Cloud e onboarding

| Funzione dell'originale | ClineWin |
|---|---|
| Passaggio di una sessione al cloud, repository, branch e modelli cloud | ❌ |
| Onboarding al primo avvio (accesso, chiave API, GitHub, importazione) | ❌ |
| Hub condiviso con la CLI (`get_hub_status`, `restart_hub`) | ➖ non serve: il backend è nativo |

## Aggiunte di ClineWin, non presenti nell'originale

- Barra dei menu classica di Windows.
- Interfaccia in italiano.
- Backend in C#, senza sidecar e senza Node/Bun.
- Chiavi API nella Gestione credenziali di Windows.

## Priorità suggerite

1. **Composer:** `@` per i file, allegati e immagini, cerca sessioni e rinomina. Sono le mancanze che si notano di più nell'uso quotidiano.
2. **Git:** pannello delle modifiche e source control, cambio di branch, worktree. I pulsanti esistono già e oggi non fanno nulla.
3. **Personalizza:** regole del progetto e server MCP. Sono le estensioni più usate.
4. **Preferenze:** applicare quelle già salvate (accento, testo, scorciatoie) e collegare il pulsante Terminale.
5. **Account Cline vero, onboarding, cloud.** Dipendono dai servizi di Cline.
