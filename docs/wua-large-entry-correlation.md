# OmegaDrive — correlazione WUA con grandi liste di dispositivi (#176, #232)

## Problema individuato in v0.0.83

L'API Windows Update Agent espone, per gli aggiornamenti `Type=Driver`, una collezione di record `IWindowsDriverUpdateEntry` attraverso `IWindowsDriverUpdate4.WindowsDriverUpdateEntries`. Ciascun record può contenere un Hardware ID **oppure un Compatible ID** per cui il pacchetto è applicabile.

Tre limiti indipendenti nel codice OmegaDrive si fermavano ai **primi 128** identificativi:

1. `WindowsDriverEntryIdReader.ReadAdditionalIds` non leggeva il record 129 e successivi;
2. `WindowsDriverCandidateMerge.MergeDeviceIds` scartava altri ID quando più ricerche WUA restituivano lo stesso GUID;
3. `DriverUpdateMatcher.MatchStrength` ignorava tutto dopo i primi 128 `AdditionalHardwareIds`.

Quindi un aggiornamento **già individuato da WUA** poteva non essere mostrato come correlato alla GPU NVIDIA o alla periferica Intel/Killer locale, pur contenendo il suo ID nelle voci successive. Il GUID era comunque uno solo: non significa che la collezione contenga centinaia di driver distinti.

Fonti ufficiali:
- Microsoft: [IWindowsDriverUpdate4](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-iwindowsdriverupdate4).
- Microsoft: [IWindowsDriverUpdateEntry.DriverHardwareID](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iwindowsdriverupdateentry-get_driverhardwareid).

## Implementazione

- Introdotto `WuaDriverEntryLimits.MaximumAdditionalIds = 512` nel Core, **unico limite** usato dal lettore COM, dalla fusione delle fonti e dal matching; massimo **512 caratteri per ID**.
- `WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage` legge fino a 512 record della collezione COM senza modificare Windows. Mantiene deduplicazione case-insensitive, validazione del bus PnP e annullamento. Dalla correzione #188, su errore di **una singola entry** COM conserva le altre associazioni già validate e continua la lettura bounded; l'elenco viene marcato incompleto. Una collezione del tutto non disponibile resta vuota e incompleta. `ReadAdditionalIds` resta disponibile per retrocompatibilità.
- Se WUA dichiara **più di 512 voci**, o se l'interfaccia COM opzionale non è completamente leggibile, il risultato viene segnalato come **associazioni PnP incomplete**. Non si presenta la lista parziale come esaustiva.
- `DriverUpdateInfo.AdditionalHardwareIdsIncomplete` è un **campo opzionale `bool=false`** compatibile con JSON IPC delle versioni precedenti; non cambia il significato di `DriverUpdateInfo`, la firma del GUID o la disponibilità dell'aggiornamento.
- `WindowsDriverCandidateMerge` unisce fino a 512 altri ID per **lo stesso GUID**, segnala troncamenti cumulativi e non incrementa il numero di aggiornamenti distinti; normalizza anche nuove collezioni non attendibili che superano il limite.
- `DriverUpdateMatcher` controlla fino a 512 associazioni aggiuntive con **uguaglianza esatta** contro Hardware/Compatible ID dell'inventario. Non indovina ID da vendor, nome dispositivo o prefisso.
- L'interfaccia mostra `associazioni hardware WUA incomplete` nei metadati dell'aggiornamento quando necessario. Il report copiabile registra soltanto il **numero di candidati** con associazioni incomplete: nessun Hardware ID, Instance ID, seriale o GUID.
- **Nessun cambiamento a `QuickUpdatePolicy`, `DriverUpdateSelectionPolicy`, `Aggiorna tutto`, `InstallCore`, download o staging**: il matching aggiuntivo non sostituisce rank SetupAPI o compatibilità OEM.

## Test automatici

- [x] Simulazione di un WUA update con **220+ record**, GPU NVIDIA RTX 4060 e Intel/Killer oltre la posizione 128, corrispondenza esatta nel Core.
- [x] Identificativi PnP simili per prefisso o versione, ma non identici, non vengono correlati.
- [x] Due pass WUA riportano lo stesso GUID e centinaia di ID: il risultato resta **un solo aggiornamento**, con i dispositivi successivi correlati.
- [x] Merge fino a 512 e overflow con flag `AdditionalHardwareIdsIncomplete`, anche per nuovi input e sorgenti duplicate.
- [x] Collezione COM oltre il limite, interfaccia opzionale assente, errore COM dopo oltre 128 voci, cancellazione nel mezzo e ID malformati.
- [x] Serializzazione JSON nuova/legacy, report aggregato senza identificativi privati e validità selezione WUA invariata.
- [ ] CI Windows .NET 10: Core/Service xUnit, build, WPF/IPC.
- [ ] QA reale PCSpecialist Recoil 16: verificare quanti `WindowsDriverUpdateEntries` WUA restituisce in ogni aggiornamento, quante associazioni PnP risultano recuperate, correlazioni NVIDIA/Intel/Killer; **mai pubblicare Hardware/Instance ID in issue**.
- [ ] Verifica delle versioni OEM autentiche, firma CAT e ranking SetupAPI (#130).

## Estensione oltre 512 e priorità all'ID primario di ogni fonte WUA (#232)

La lettura precedente si fermava dopo **512 record COM**, anche quando
l'ID della GPU NVIDIA, Wi-Fi Intel/Killer o audio Realtek appariva più avanti
nella lista di un **aggiornamento già restituito da Windows Update**.
La fusione di più ricerche WUA aveva inoltre un caso limite: quando i 512 ID
secondari della fonte preferita occupavano tutti gli slot, l'Hardware ID
principale di un'altra fonte che riportava **lo stesso GUID di update**
veniva scartato.

Da questa correzione il comportamento è:

- **2.048 record COM massimo** esaminati tramite
  `IWindowsDriverUpdate4.WindowsDriverUpdateEntries`. Non vengono
  registrati servizi, installati driver o avviati processi.
- **1.024 identificativi PnP distinti massimo** associati a un update
  nel servizio, nel merge fra fonti, nel trasporto JSON/IPC e nella UI.
  Valori duplicati case-insensitive non consumano gli slot; lunghezza
  singolo ID fino a **512 caratteri** (controlli di sintassi conservati).
- Se ci sono più di 2.048 record, più di 1.024 ID unici, record COM
  non leggibili o ID non validi, viene impostato
  `AdditionalHardwareIdsIncomplete=true`. La mancata associazione
  **non può essere interpretata come assenza verificata**.
- Durante il merge di **uno stesso GUID** fra due ricerche WUA, l'ID
  principale della fonte preferita rimane il `HardwareId` di output;
  l'ID primario dell'altra fonte viene salvato **prima** degli ID
  aggiuntivi, così non scompare per saturazione del limite. Un GUID
  continua a rappresentare **un solo aggiornamento**.
- Se Windows Update non ha restituito un determinato GUID, nessuna
  delle nuove associazioni può inventare quel pacchetto. Le fonti
  `Type=Software`, le ricerche parziali, i link OEM e le notizie
  NVIDIA **non** diventano driver disponibili.

La prova è sintetica: test su NVIDIA GPU/Killer Wi-Fi/Realtek HD Audio
oltre la voce 512, 1.100 duplicati seguiti da modelli pertinenti,
collezioni sopra 2.048, overflow a 1.024, errore COM oltre la voce
512, merge con ID primario alternativo sia ordinario sia avanzato,
de-duplica GUID e preserva le policy di aggiornamento.

**Limite importante:** la correlazione aggiuntiva può spiegare i driver
non associati in OmegaDrive, ma non aumenta di per sé il numero di
offerte WUA `Type=Driver`. Il confronto 2 vs 8 su PCSpecialist
Recoil 16 va ancora misurato sul PC reale (#91, #136).

## Correzione #188: entry COM non leggibile, ma altre associazioni valide

Un aggiornamento già confermato WUA può contenere centinaia di entry. In
precedenza una sola proprietà COM non leggibile al record 300 cancellava
tutte le associazioni raccolte correttamente, anche per la GPU NVIDIA o
la scheda Intel/Killer. Ora il lettore:

- conserva soltanto gli ID PnP effettivamente letti e validati, senza
  estrarre inferenze per prefisso/nome;
- continua il confronto delle entry successive fino al massimo di 512;
- imposta `AdditionalHardwareIdsIncomplete` per errori di lettura,
  valori non validi o interfaccia/collezione non disponibili;
- rifiuta Instance ID con seconda barra inversa, percorsi `..`,
  spazi/controlli e codici bidi;
- mantiene la cancellazione come errore non recuperabile (non pubblica
  successi artificiali); non modifica WUA GUID, policy o installazioni.

Regressioni CI: errore sulla entry 300, lettura NVIDIA valida prima,
Killer valido dopo, proprietà `DriverHardwareID` non leggibile, input
malformati e collezione nulla, cap e cancellazione. **Il flag incompleto
non prova che tutti gli aggiornamenti disponibili siano stati trovati**.

## Limite importante

Questo miglioramento può **recuperare associazioni hardware** su pacchetti già restituiti da Windows Update. Non crea pacchetti nuovi, non consulta una fonte nuova e **non dimostra** che OmegaDrive troverà otto aggiornamenti sul Recoil 16. La verifica del divario 2 vs 8 rimane aperta (#91 e #136).

Rif. #91, #99, #100, #130, #136, #176.
