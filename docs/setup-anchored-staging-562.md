# Setup: pin Win32 degli antenati durante l'installazione (#562)

## Rischio
I controlli path-based di #528/#561 respingono junction e symlink già presenti,
ma da soli non impediscono la rinomina/sostituzione concorrente di un parent
durante la scrittura del payload temporaneo.

## Mitigazione delimitata
`SetupDirectoryAnchor.Open` apre gli antenati **da volume root a leaf** con
`CreateFileW`, `FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS` e
permessi di lettura attributi. Ogni handle è controllato tramite
`GetFileInformationByHandle` per rifiutare reparse point o nodi che
non sono directory. Tutti gli handle restano aperti **senza**
`FILE_SHARE_DELETE`: Windows deve quindi rifiutare rename/delete degli
antenati ancora aperti.

Il setup mantiene il guard per le operazioni elevate di installazione,
lo libera prima della pulizia della directory temporanea. L'estrazione
ZIP mantiene un guard della root e usa un guard aggiuntivo sul parent
di ogni file durante la scrittura. Gli errori Win32 falliscono chiusi
senza usare fallback path-only; gli errori non includono percorsi locali.
Il guard rifiuta percorsi UNC/device namespace su Windows.

I test sintetici Windows verificano che la rinomina fallisca durante
il guard e riesca dopo il suo Dispose, il rifiuto di un antenato symlink,
il percorso mancante senza effetti collaterali e la normale estrazione.

## Limiti residui della #529
Questa **non è** una creazione di file relativa agli handle: tra la
creazione di una nuova directory annidata e l'acquisizione del suo handle
può ancora esserci una race. Non si verifica ancora una **ACL privata**
con owner attendibile sulla root. Il pin deve essere verificato anche in
un vero Windows 11 e con swap concorrenti intenzionali, prima di
dichiarare protezione TOCTOU completa. Rimangono #529 e il QA #85.
Non cambia l'installazione dei driver OEM né le regole di firma CAT/INF.
