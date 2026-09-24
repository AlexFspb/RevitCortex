# RevitCortex 2026 — Guida installazione

Questa versione è un fork di `LuDattilo/RevitCortex` mantenuto specificamente per **Autodesk Revit 2026**.

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

## Conferma delle operazioni ordinarie

Ogni operazione che richiede conferma mostra un solo pulsante per autorizzarla una volta. L'esecuzione automatica dopo 3 secondi è selezionata per impostazione predefinita; disattivandola, la finestra attende il consenso manuale. La scelta vale fino alla chiusura di Revit. La croce o Escape annullano l'operazione corrente. Le vecchie opzioni di consenso per 2 minuti o senza scadenza e la finestra «Auto mode ON» sono rimosse. Le conferme C# restano separate e richiedono di abilitare esplicitamente il loro auto-run.

## Esecuzione C# (`send_code_to_revit`)

L'esecuzione di codice C# è una funzione avanzata e resta **disabilitata per impostazione predefinita**. Può essere abilitata da **Settings → Tools**.

Quando un C# script sta per essere eseguito, RevitCortex mostra una finestra di conferma critica con i pulsanti **Yes** e **No**.

La finestra contiene inoltre l'opzione **Allow auto-run**:

- se non è selezionata, lo script parte solo dopo un click manuale su **Yes**;
- se viene selezionata, il pulsante mostra un conto alla rovescia di **3 secondi**;
- allo scadere dei 3 secondi lo script viene approvato automaticamente;
- **Yes** e **No** restano utilizzabili durante il conto alla rovescia;
- l'opzione vale solo per la sessione Revit corrente e viene azzerata alla chiusura di Revit.

La validazione sandbox e l'audit del tool restano attivi.

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
