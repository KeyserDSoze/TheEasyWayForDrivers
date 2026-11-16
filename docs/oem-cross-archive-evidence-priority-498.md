# Priorità unificata per gli INF OEM in più archivi (#498)

## Diagnosi
Il confronto di un singolo ZIP/cartella usa già in
`LocalOemDriverComparison.Priority` le penalità firmware, la data
`DriverVer` e i segnali di compatibilità PnP. La funzione
`LocalOemArchiveBatchSummary.Priority` del riepilogo di 24 ZIP/CAB
era però una copia *obsoleta* che ignorava firmware e date INF.

A pari forza Hardware ID, una versione firmware `999.0.0.1` poteva
nascondere un driver ordinario da un diverso archivio; oppure un INF
con data 2022/numero 99 poteva nascondere uno del 2026/numero 33.
L'esito era particolarmente fuorviante sul bundle PCSpecialist in cui
i pacchetti GPU, audio, Wi-Fi e firmware sono forniti separatamente.

## Correzione
- Il punteggio diagnostico è ora definito **una sola volta** nel
  comparer OEM locale e riutilizzato dalla selezione cross-archive.
  Le priorità PnP, firmware (revisione separata), metadata Windows
  verificati, data DriverVer e versione hanno quindi esattamente la
  stessa semantica.
- La deduplicazione per `DeviceOrdinal` mantiene un risultato per
  dispositivo PnP anche con 24 archivi, fairness di 80 risultati,
  conteggi distinti vs associazioni, origine del pacchetto anonima.
- Aggiunto `FirmwareInfShown` e nota esplicita sui firmware fra le
  righe selezionate, mai conteggiati come driver ordinari nuovi.
- Test con due ZIP reali sintetici e ordine invertito, NVIDIA firmware
  vs Display, vecchia data INF/numero maggiore, Killer Wi-Fi/Realtek,
  matching exact vs compatible e compatibilità legacy senza ordinal.

## Limiti
È un *ranking di presentazione*, **non** il rank driver Windows
SetupAPI. Non verifica firme CAT, WHQL, payload SYS/DLL, staging o
possibilità di installazione; gli archivi non vengono eseguiti.
Hardware QA Recoil16 (#136/#91/#85), completezza catalogo #120,
verifica OEM esterni #130 e firma release #283 restano da chiudere.
