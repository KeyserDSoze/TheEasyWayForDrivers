# OmegaDrive — ZIP e CAB annidati nei pacchetti OEM (#218)

## Perché questa funzione

Fino alla v0.0.103 OmegaDrive leggeva gli INF negli ZIP/CAB
selezionati, anche quando distribuiti come overlay ZIP standard
di un EXE PE. Ma un comune packaging OEM può essere:

```text
PCSpecialist-drivers.zip
  NVIDIA/Graphics.zip -> Display/driver.inf
  Network/Killer.cab -> net/driver.inf
  Audio/Realtek.zip -> Realtek/driver.inf
```

Prima gli INF dentro `Graphics.zip` o `Killer.cab`
non venivano raggiunti, producendo possibili falsi zero.
Ora il confronto in sola lettura analizza **un livello**
di archivi ZIP/CAB all'interno di ZIP o overlay PE-ZIP.

## Flusso

Dopo **Analizza il PC**, aprire **Dettagli avanzati** e
selezionare **Confronta ZIP / CAB / EXE multipli**.
Si può scegliere da 1 a 12 pacchetti locali del produttore.
Gli EXE sono supportati **soltanto** quando contengono uno
ZIP standard dopo le sezioni PE; EXE 7z, NSIS e Inno non
vengono eseguiti né interpretati.

1. Valida lo ZIP esterno con `OemArchiveInspector`, inclusi
   percorso, symlink, duplicati, dimensioni e compressione.
2. Conserva gli INF direttamente presenti nello ZIP esterno.
3. Individua massimo **otto ZIP/CAB interni** per pacchetto,
   con max **512 MiB per file** e **1 GiB totale**.
4. Copia **uno alla volta** gli archivi interni dentro una
   directory temporanea casuale, verificando realmente il
   numero di byte letti. I file temporanei sono eliminati
   in `finally`. Non viene eseguito nessun file scaricato.
5. Per ZIP interni riusa `LocalOemDriverComparison`:
   INF letti in streaming, 96 INF max, 4 MiB per INF,
   24 MiB totali INF, limitazioni ZipBomb/traversal.
   Nessuna estrazione o esecuzione di file SYS/DLL.
6. Per CAB interni riusa `LocalOemCabComparison`:
   indice CAB esaminato prima dell'estrazione; solo lo
   **`expand.exe` di Windows** per INF temporanei;
   nessun EXE del produttore viene avviato.
7. Unisce tutte le corrispondenze tramite gli **indici PnP
   anonimi** dell'inventario, non per nome o versione:
   una GPU NVIDIA presente in più pacchetti compare una
   volta sola, scegliendo il migliore indizio disponibile.
   Massimo **80 dispositivi mostrati**, inclusi conteggi
   esatti di corrispondenze oltre il limite.
8. In caso di CAB/ZIP corrotto o fuori limite fallisce
   **l'intero confronto**, mai "zero driver" come falso
   successo. Se un ZIP interno contiene un ulteriore
   ZIP/CAB (secondo livello), NON lo attraversa e riporta
   esplicitamente che la copertura è **parziale**.

La selezione multipla resta atomica: un pacchetto non
verificabile non pubblica il resto come se fosse esaustivo.

## Limiti e sicurezza

- Nessun download o richiesta a PCSpecialist: numero ordine
  e percorsi rimangono locali; nei risultati compaiono solo
  etichette anonimizzate degli archivi.
- Un match di Hardware ID, piattaforma Windows e versione
  più alta è solo una **possibile corrispondenza**.
  La firma CAT, integrità SYS/DLL, ranking SetupAPI e
  applicabilità OEM finale **non sono verificati**.
- Nessuna modifica alla lista WUA `DriverUpdateInfo`,
  ai pulsanti di installazione o a **Aggiorna tutto**;
  `IsInstallable=false` e `IsVerifiedCompatible=false`.
- ZIP-in-ZIP-in-ZIP, MSI, installer proprietari e 7z non
  vengono interpretati. Quando presenti pacchetti più
  profondi, OmegaDrive segnala la copertura parziale.
- I limiti di spazio e timeout restano quelli dei
  confronti originali (batch: 12 archivi e 4 GiB cumulativi,
  timeout 5 minuti; CAB: 120 secondi).

## Test e QA reale

La suite verifica pacchetti sintetici NVIDIA RTX 4060,
Intel/Killer Wi-Fi, Realtek audio, ZIP esterno con soli ZIP
interni, ZIP+CAB interno generato tramite `makecab.exe`
sul runner Windows, ZIP di overlay EXE PE, ordinamento
delle versioni, deduplica GPU nel medesimo e in più
pacchetti, ZIP corrotti, traversal, 9 figli, profondità
oltre il primo livello, CAB senza INF, cancellazione.

La CI Windows verifica `net10.0` Core/Service/Desktop,
test xUnit e contratti UI/IPC. Non è una prova runtime
sul Recoil 16: i pacchetti OEM reali e il confronto
**Driver Easy 8 vs OmegaDrive 2** restano da verificare
nelle issue #91 e #136.

Per una comparazione attendibile, scaricare pacchetti
ufficiali dal portale OEM e confrontare gli INF; la
presenza di un driver nel pacchetto non dimostra che
Windows debba installarlo.
