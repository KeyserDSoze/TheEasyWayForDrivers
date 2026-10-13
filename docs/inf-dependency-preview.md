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

Sono supportati i casi semplici senza token `%Strings%` nei nomi di file o percorsi. Per le mappe dei file sorgente, OmegaDrive sceglie la versione `SourceDisksFiles.<architettura>` pertinente, con ricaduta sulla sezione generica. I percorsi sono **relativi**, senza `..`, drive assoluti, UNC e interpolazione di script. Il parsing è limitato a **1 MiB di testo INF**, **160 sezioni** e **80 riferimenti**; l'inventario dei file mantiene i limiti della #54.

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
