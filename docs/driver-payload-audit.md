# Audit parziale dei file del driver — Discovery+ (Issue #54)

## Motivazione

Microsoft documenta che il catalogo firmato del driver contiene gli hash dei file del pacchetto e che Windows, prima di effettuare lo staging nel Driver Store, verifica la presenza e la coerenza dei file referenziati dall'INF. Fonte: [Driver Store](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/driver-store), [Catalog files and digital signatures](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/catalog-files), [SignTool](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/signtool).

## Funzionamento read-only

Dopo **Analizza INF e integrità**, quando sono presenti INF e CAT locali, l'utente può premere **Verifica file SYS/DLL/INF nella cartella**. L'operazione richiede conferma esplicita e Windows SDK SignTool già presente; non installa l'SDK, non esegue file del pacchetto e non modifica il Driver Store.

L'analizzatore visita in modo limitato la cartella dell'INF e le sottocartelle. Tra i file *presenti* sceglie le estensioni INF, SYS, DLL, EXE, BIN, DAT e calcola SHA-256 per ciascuno. Il digest **senza hash esterno autenticato** serve esclusivamente per identificare i byte. Prova la membership di ciascun candidato rispetto al **CAT dichiarato dall'INF** utilizzando `SignTool verify /kp /c <catalogo.cat> <file>` (Windows SDK ufficiale). Per ogni file mostra:

- **Verificato nel CAT:** SignTool termina con codice 0 su quel preciso file e con quel CAT.
- **Rifiutato:** SignTool termina con codice diverso da 0.
- **Non verificato:** Windows SDK assente, timeout, errore di accesso o esito inconcludente.

## Limiti e protezioni

- File INF/CAT locali, stessa cartella radice, niente UNC o symlink/reparse point in file e cartelle; niente shell, path interpolati in script o download.
- Massimo 256 file inventariati, **24 file candidati**, 64 MiB per singolo file candidato, 256 MiB aggregati; file INF 1 MiB e CAT 32 MiB nel validatore SignTool. Timeout dell'intera analisi fino a 2 minuti.
- Qualsiasi superamento dei limiti, elemento non sicuro o inventario ambiguo causa fallimento prudente senza attestazione parziale positiva.
- Hash INF atteso: se l'utente inserisce un digest che non corrisponde o è malformato, l'audit viene rifiutato.
- I file di tipo non elencato vengono segnalati come **esclusi dal campo di indagine**, non come implicitamente verificati.
- **NON è possibile dedurre la completezza**: non è implementata una risoluzione rigorosa delle direttive INF `CopyFiles`, `Include`, `Needs`, `SourceDisksFiles`, `CoInstallers`, payload firmware, varianti architettura/OS né verifica di file mancanti dal disco. I cambi di file concorrenti durante la lettura rimangono un rischio TOCTOU, da affrontare prima di eventuali automatismi. Anche il fatto che tutti i file *presenti e selezionati* risultino nel CAT non certifica che siano tutti i file necessari.
- **Nessun risultato abilita installazione automatica OEM.** L'installabilità richiede una security review, Windows 11 hardware QA (#5) e il completamento della issue #46.

## QA richiesta

Con un pacchetto reale Dell/HP/Lenovo o driver OEM firmato, provare un INF/CAT ufficiale e confrontare manualmente il risultato di ogni SYS/DLL con SignTool. Provare CAT estraneo, file modificato, sottocartelle, link/junction, percorsi UNC, SDK mancante, più di 24 candidati, file oltre 64 MiB e cartella oltre 256 file. **Non installare driver durante la prova.**
