# Discovery+ — anteprima dipendenze INF (Issue #56)

## Perché serve

L'audit introdotto nella v0.0.33 verifica i file **già presenti** nella cartella di un driver rispetto al CAT ufficiale mediante Microsoft SignTool. Un pacchetto può tuttavia avere file assenti, che non possono essere individuati attraverso un inventario dei soli file presenti.

Microsoft descrive:
- [INF CopyFiles](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-copyfiles-directive): `CopyFiles=@filename` o `CopyFiles=file-list-section`; nelle liste `destination-file-name,source-file-name` la **seconda voce**, quando presente, rappresenta il file sorgente.
- [INF SourceDisksFiles](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-sourcedisksfiles-section): relazione fra nome file, disk ID e sottocartella; varianti `SourceDisksFiles.amd64`, `.arm64` e `.x86` non usano il prefisso `NT`.
- [INF SourceDisksNames](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-sourcedisksnames-section): percorso di base dei file nel disco di distribuzione, eventuali archivi CAB.
- [INF Include/Needs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-ddinstall-section): dipendenze da sezioni di altri INF o da sezioni del sistema.

## Cosa fa ora OmegaDrive

1. L'utente sceglie un file INF locale con un CAT.
2. Conferma **Verifica file SYS/DLL/INF nella cartella**.
3. Oltre alla firma CAT per i file locali, la nuova classe `InfDependencyPreview` analizza il testo INF e le sezioni `Manufacturer` → `Models` per ricavare possibili sezioni d'installazione.
4. Per le direttive `CopyFiles` riconoscibili distingue i singoli file diretti, le liste e gli eventuali file sorgente **rinominati**; incrocia `SourceDisksFiles`, `SourceDisksNames`, cartelle e inventario locale.
5. Mostra per ogni riferimento **presente**, **possibilmente mancante** o **non risolto**, oltre agli avvisi per `Include`/`Needs`, `CopyINF`, CAB o sezioni non interpretabili.

Sono supportati i casi semplici senza token `%Strings%` nei nomi di file o percorsi. Per le mappe dei file sorgente, OmegaDrive sceglie la versione `SourceDisksFiles.<architettura>` pertinente, con ricaduta sulla sezione generica. I percorsi sono **relativi**, senza `..`, drive assoluti, UNC e interpolazione di script. Il parsing è limitato a **4 MiB di testo INF**, **12.000 righe fisiche**, **4.096 caratteri per riga logica**, **160 sezioni** e **80 riferimenti**; l'inventario dei file mantiene i limiti della #54.

## Cosa NON dimostra

**Non è un'implementazione di Windows SetupAPI e non certifica il pacchetto completo.** In particolare:

- Le sezioni DDInstall vengono considerate **potenziali**, non selezionate con il ranking PnP reale, il sistema operativo o gli Hardware ID.
- `Include`, `Needs`, archivi CAB, dipendenze di sistema, estensioni CoInstaller, `CopyINF`, `ServiceBinary`, `AddComponent`, file generati o in altre sedi non vengono risolti con certezza.
- Non gestisce tutte le decorazioni TargetOSVersion / ProductType / SuiteMask, `Strings`, path avanzati, `LayoutFile` o altre direttive INF. Uno **zero** nei file mancanti NON significa pacchetto completo.
- I file nel CAT firmato sono verificati separatamente; una dipendenza trovata su disco non dimostra che sia firmata, che sia necessaria per questo dispositivo o che la sua installazione sia corretta.
- Il controllo resta in sola lettura: **nessun file viene scaricato, copiato nel Driver Store o installato**.

Issue #46 resta aperta per integrità e policy complete; la #5 resta aperta per test reali Windows 11. Per eventuale installazione OEM servono verifica completa tramite le API Windows native, sicurezza, compatibilità e QA.


## Aggiornamento incrementale — Needs locali (#59)

L'anteprima segue ora le sezioni richiamate tramite `Needs` **solo nello stesso INF** e **solo in assenza di `Include` nella sezione chiamante**. I file `CopyFiles` rilevati nelle sezioni richiamate confluiscono nell'inventario potenziale; sono applicati limiti di profondità (12) e numero di sezioni visitate (80), rilevamento cicli e warning in caso di riferimenti invalidi o non trovati. Questo comportamento è euristico e non seleziona DDInstall né emula SetupAPI.

Se una sezione contiene `Include`, le sue `Needs` non vengono percorse localmente, anche se esiste una sezione con lo stesso nome: potrebbero puntare a un INF esterno o di sistema. Nessun INF esterno viene letto o scaricato. Sezioni condizionali, `Strings`, `CopyINF`, CAB e altri payload restano non risolti come specificato sopra. `IsCompletePackage` rimane sempre **false**. Nessuna variazione ai permessi di installazione.

## Incremento Discovery+ Intelligence (#61–#64)

- `[Strings]`: risoluzione **solo letterale, singolo livello** di token `%Name%` usati come nomi di file e sezioni. Nessun token ricorsivo, nessuna ricerca implicita di lingue/locale, nessuna interpolazione di percorsi non sicuri; il parser mantiene i limiti preesistenti. Le stringhe localizzate e i formati più complessi restano fuori ambito.
- `AddService`/`ServiceBinary`: intercetta le sezioni servizio indicate da `AddService` e produce avvisi su `ServiceBinary`. Il suo valore è tipicamente un **percorso di destinazione** nel sistema e **non** viene trasformato in un presunto file sorgente mancante.
- Decorazioni Models con segmenti aggiuntivi: `TargetOSVersion`, `ProductType` e `SuiteMask` non sono considerati superati solo perché l'architettura coincide. Il report avverte che la selezione del sistema operativo non è stata verificata.
- Provenienza: ogni referenza riconosciuta include la sezione INF originaria e la direttiva `CopyFiles`; Discovery+ espone tale informazione nel report testuale.

L'implementazione resta un'anteprima **non normativa**. Una risoluzione definitiva di varianti localizzate, DDInstall, ServiceBinary e condizioni Windows richiede strumenti Windows e fixture reali. Questo incremento **non** modifica i permessi di installazione.


## Report raggruppato Discovery+ (#64)

Il riepilogo dell'audit elenca ora separatamente **possibili file mancanti**, **origini non risolte** e **file presenti**. Per ciascuna voce riporta la sezione/direttiva INF d'origine quando nota; include inoltre il conteggio delle avvertenze. Questa suddivisione facilita la revisione manuale ma **non produce un giudizio di idoneità**: il risultato conserva `IsCompletePackage=false` e `RequiresManualReview=true`, anche quando non sono individuati file mancanti. L'audit CAT per-file resta separato dalla mera presenza di un file.


## Correzione parsing righe logiche (#395, 12 novembre 2026)

L'anteprima delle dipendenze ora condivide `InfLogicalLines.Read` con i parser PnP: ricompone direttive `CopyFiles`/`Needs` continuate con `\` finale e distingue i commenti `;` soltanto **fuori dalle virgolette e dai token di stringa**. Un descrittore `SourceDisksNames` come `1="OEM; media",,,drivers` non deve troncare la sottocartella `drivers`, altrimenti un file realmente presente risulterebbe erroneamente mancante. Quote non bilanciate e continuazioni interrotte sono respinte anziché trattate come indizi validi.

I limiti fisici di 12.000 linee e di 4.096 caratteri per direttiva logica sono mantenuti indipendentemente dai limiti del lettore INF comune. Le fixture sintetiche NVIDIA/Killer controllano sorgenti `CopyFiles`, percorsi relativi e `Needs` locali; la presenza è solo una evidenza di inventario **non normativa** e `IsCompletePackage` resta **false**. Sono ancora fuori ambito firme CAT, selezione DDInstall/SetupAPI effettiva, provenienza dei pacchetti e installazioni OEM.


## SourceDisksNames: campi quotati e archivi CAB (#396, 12 novembre 2026)

Le direttive `SourceDisksNames` e `SourceDisksFiles` sono separate da virgole **solo fuori dalle virgolette**. Un'etichetta descrittiva come `1="Notebook, NVIDIA/Killer",,,drivers` non deve spostare il quarto campo (directory sorgente). Il parser distingue inoltre un vero tag di CAB anche se è scritto tra virgolette (`"driverpack.cab"`): il payload nel CAB resta **non risolto** persino quando un file con lo stesso nome è visibile nel folder.

Il separatore accetta virgolette raddoppiate all'interno delle descrizioni e i path relativi quotati, ma respinge quote mal poste, percorsi non sicuri e liste di campi oltre il limite. Gli stati non risolvibili non vengono promossi a `Present` usando un mapping generico precedente. I nuovi test coprono NVIDIA/Killer, CAB, percorsi OEM e input malformati; la verifica resta **solo locale e non normativa**, senza installazione, ranking SetupAPI o attestazione CAT.


## Mappature duplicate e override architetturali fail-closed (#397, 12 novembre 2026)

Un `SourceDisksNames.amd64` con directory illeggibile **non eredita più** la directory dell'omonimo disk ID generico: l'origine viene marcata `Unresolved`, con warning. Le ripetizioni dello stesso disk ID nella medesima sezione `SourceDisksNames` o dello stesso nome file nella medesima `SourceDisksFiles` (ignorando maiuscole/minuscole) sono ambigue anche se le righe riportano dati identici: non viene attribuita una presenza basata sull'ultima riga. L'esito rimane prudente anche dopo una terza ripetizione.

Una dichiarazione valida nella sezione decorata per la CPU/architettura corrente **può ancora prevalere** sulla corrispondente dichiarazione generica: non sono trattate come duplicati fra sezioni. Gli esiti `Present` indicano solo che un file con quel nome si trova nell'inventario locale, non garantiscono che il driver sia completo, applicabile, firmato, pubblicato dall'OEM o installabile. `IsCompletePackage` rimane `false`. Nessun file viene eseguito, copiato nel Driver Store o installato.


## Token `[Strings]` duplicati: audit fail-closed (#398, 12 novembre 2026)

Quando un nome nella sezione `[Strings]` è dichiarato più volte, anche con case diverso o valore identico, l'anteprima non assume più che la **prima dichiarazione** sia quella corretta. Rimuove la sostituzione e conserva un warning anonimo, anche se compare un terzo duplicato. Un `CopyFiles=@%Binary%` non risolto non genera una falsa evidenza di file `Present` e non entra tra i file proposti al controllo CAT read-only; un token valido e univoco continua invece a essere analizzato normalmente.

Il warning non include il nome o il contenuto del token e non invia informazioni del PC. Questa precauzione non implementa la semantica completa delle sezioni `Strings` localizzate, del ranking Windows o delle dipendenze multi-INF. La presenza di un file, comunque, non equivale a provenienza OEM, firma CAT o installabilità; `IsCompletePackage` resta `false`.


## Sicurezza dei percorsi `SourceDisks`: UNC e separatori multipli (#399, 12 novembre 2026)

L'analisi INF scarta riferimenti di cartella `SourceDisksNames` o `SourceDisksFiles` che iniziano con **due separatori** (come `\\\\server\\share`, `//server/share` o varianti miste). Non li converte più in percorsi relativi rimuovendo tutti gli slash iniziali: un file locale con nome uguale non è prova che il file UNC sia presente o referenziato in maniera verificabile. Il riferimento diventa `Unresolved` con un avviso che non espone il percorso.

Resta supportato il **singolo** separatore iniziale delle directory sorgente INF convenzionali, per esempio `\\WinNT` o `\\drivers`, oltre ai percorsi relativi ordinari. I test includono un audit di cartella reale *sintetica* con un SYS omonimo: la mappatura UNC non produce candidature di verifica CAT. Questo comportamento non interpreta il formato Windows completo, non accede a condivisioni di rete, non dichiara il pacchetto completo e non autorizza installazioni.


## Audit manuale file OEM: decoder condiviso e INF fino a 4 MiB (#400, 12 novembre 2026)

Il comando **Verifica file SYS/DLL/INF nella cartella** prima usava `File.ReadAllText` e respingeva ogni INF oltre **1 MiB**, a differenza del confronto OEM ZIP/CAB/cartelle. Ora legge l'INF selezionato attraverso `InfDependencyPreview.AnalyzeBounded` → `OemInfTextDecoder.ReadBounded`, con **limite di byte 4 MiB**, riconoscimento conservativo di UTF-8, UTF-16 LE/BE (anche i casi senza BOM strettamente riconosciuti) e fallback ANSI limitato alla grammatica ASCII sicura. Il decoder respinge UTF-32, Unicode malformato e dati binari, invece di correggerli silenziosamente.

Sono invariati i limiti di inventario manuale (256 elementi, 24 candidature, limiti dimensionali), parsing di righe/sezioni/riferimenti INF e verifica Windows CAT separata. I test sintetici coprono NVIDIA >1 MiB, Intel Killer UTF-16 BE/ANSI, dipendenze assenti e file malformati. La presenza di un file non prova firma, provenienza OEM, selezione SetupAPI, aggiornamento disponibile o installabilità: `IsCompletePackage` resta **false**. I collaudi su INF e CAT OEM reali, hardware Recoil 16 e confronto Driver Easy rimangono aperti.


## ServiceBinary: provenienza e destinazione, non sorgente OEM (#62, 12 novembre 2026)

L'anteprima espone una sezione diagnostica distinta **`SERVICEBINARY: DESTINAZIONI, NON SORGENTI`** per i riferimenti `AddService` nelle sezioni di installazione potenziali: registra la sezione che dichiara il servizio, la sezione di installazione del servizio e, solo se il percorso segue la convenzione prudente `%DirID%\\file.sys` (DirID numerico e path relativo sicuro), il **nome finale di destinazione**. Percorsi assoluti, UNC, variabili non numeriche, traversal, `ServiceBinary` duplicati e `AddService` ripetuti restano non risolti. Nessun percorso Windows di destinazione viene copiato nel report.

Queste evidenze **non** confluiscono nella lista `CopyFiles`, nei contatori di file presenti/mancanti o nei candidati per la verifica CAT. Anche se `CopyFiles` indica separatamente un SYS con lo stesso nome, il nesso con il servizio non è verificato senza la selezione effettiva di DDInstall, le sezioni INF di destinazione e SetupAPI. Limite di 32 evidenze servizio per INF, con input e riferimenti oltre limite respinti. Sono state aggiunte regressioni sintetiche NVIDIA/Killer e un controllo sulla lista CAT. `IsCompletePackage` resta sempre `false`; nessun download/staging/installazione.
