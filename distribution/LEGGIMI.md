# RevitCortex 2026 — Guida installazione

Questa versione è un fork di `LuDattilo/RevitCortex` mantenuto specificamente per **Autodesk Revit 2026**.

Revit 2026 multi-instance policy (2026-10-09): the server starts once automatically at the first Idling event, including empty Revit. Manual Stop stays off until manual Start or restart. Release selects 8080 → 8888 → 8880 → 8088; Dev selects 8081 → 8889 → 8083 → 8891. Every MCP entry has an explicit fixed port; never fall back to another connection. Inspect get_connection_status/list_revit_instances for cached identity, then get_project_info or say_hello to verify the intended document. Automatic execution defaults to ON at every Revit launch for scripts and ordinary operations. The ribbon toggle and General Settings control one process-local policy. When ON there are no approval windows or countdowns; when OFF manual per-request confirmation remains. Orange indicates C# execution; blue indicates other tool execution. UI-thread work can pause blinking. No approval flags are persisted. The first C# request with no active document prepares a private metric service project. Never substitute that project for a user-named model. Shared settings must not be edited concurrently; temp scripts are isolated by process.

## Requisiti

- Autodesk Revit **2026**
- Windows 10/11
- Un client MCP compatibile, ad esempio Claude Desktop, Claude Code, Codex, Cursor o altro client MCP stdio
- Permessi amministratore per l'installazione machine-scope

Le versioni Revit 2023, 2024, 2025 e 2027 non sono supportate da questo fork.

## Installazione

1. Chiudi Revit 2026.
2. Estrai il pacchetto ZIP in una cartella locale.
3. Esegui `install.ps1` con PowerShell.
4. Segui le richieste a schermo.
5. Riavvia Revit 2026 e il client MCP.

Il plugin viene installato per Revit 2026 in una delle seguenti posizioni, in base ai permessi disponibili:

- `C:\ProgramData\Autodesk\Revit\Addins\2026\RevitCortex\`
- `%APPDATA%\Autodesk\Revit\Addins\2026\RevitCortex\`

Il server MCP viene installato in:

`%USERPROFILE%\.revitcortex\server\`

## Avvio in Revit

Dopo il riavvio di Revit 2026, usa **Cortex Switch** nel ribbon per avviare o fermare il bridge locale RevitCortex. Il servizio è disattivato per impostazione predefinita.

## Esecuzione automatica

Il comando Avtovypolnenie nella barra e l'opzione nelle impostazioni General controllano la stessa preferenza del processo Revit. Si attiva a ogni avvio. Quando attiva, le operazioni e gli script non mostrano finestre di conferma né conto alla rovescia. Quando disattiva, si richiede la conferma manuale. Le protezioni restano attive.

Cortex Switch: grigio = spento, verde = disponibile, arancione = script C#, blu = altro strumento. Il lampeggio può fermarsi durante un'operazione Revit sincrona. Non indica una percentuale di avanzamento.

## Sicurezza

Usa i tool RevitCortex dedicati quando disponibili. `send_code_to_revit` è pensato come ultima risorsa per operazioni non coperte dai tool dedicati.

Per operazioni distruttive o massive, usa `dryRun: true` quando il tool lo supporta prima dell'esecuzione reale.

## Disinstallazione

Chiudi Revit 2026 ed esegui `uninstall.ps1` con PowerShell. Lo script rimuove il plugin da Revit 2026 e il server MCP, mantenendo i dati utente in `%USERPROFILE%\.revitcortex\` salvo cancellazione manuale.

## Aggiornamenti

Questo fork **non usa il canale di aggiornamento automatico dell'upstream**, per evitare che una release originale sostituisca le personalizzazioni di questa versione. Gli aggiornamenti del fork vanno installati dal repository `AlexFspb/RevitCortex` finché non viene configurato un canale release dedicato.

## Repository

Fork: `https://github.com/AlexFspb/RevitCortex`

Upstream: `https://github.com/LuDattilo/RevitCortex`

## Licenza

Vedi `LICENSE` nel repository.

Dopo l’attivazione, il server resta acceso anche chiudendo famiglie o progetti. Senza un documento attivo, i comandi del modello restituiscono un errore finché non viene aperto un progetto. I comandi in attesa per un documento precedente vengono annullati. L’arresto manuale resta rispettato.
