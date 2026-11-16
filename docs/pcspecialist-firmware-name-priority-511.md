# PCSpecialist: etichetta prudente Firmware/BIOS nelle liste HTML (#511)

## Problema
L'anteprima di file scaricabili da HTML locale assegna categorie
**in base al solo basename**. Il riconoscimento di NVIDIA, Intel,
Killer, Realtek, Bluetooth e Control Center precedeva quello di
`Firmware`/`BIOS`. Nomi come `W11 NVIDIA GPU Firmware.exe`
diventavano `NVIDIA · grafica / Pacchetto driver da verificare` e
potevano alterare la fairness di scelta dei 96 slot preview e
l'ordine degli EXE con ZIP embedded valutati in sola lettura.

## Soluzione
- `PcSpecialistPortalSnapshot.Categorize` rileva prima di
  ogni altra categoria i token espliciti `Firmware` e `BIOS`
  nel nome sicuro, assegnando `Piattaforma / firmware` e
  `Verifica manuale necessaria`.
- Gli ordinari file NVIDIA GPU/VGA, Intel Graphics, Wi-Fi/Killer,
  Bluetooth, Realtek Audio/LAN, Control Center restano invariati.
  Anche la famiglia Intel ME mantiene la sua classificazione
  conservativa precedente.
- Non viene inferito il contenuto effettivo del package: il
  filename non è prova che il pacchetto sia firmware, né che
  sia un driver necessario o autenticato.
- Il planner locale continua a usare il medesimo classificatore
  per la fairness degli EXE, senza download, esecuzione o installazioni.

## Regressioni
`PcSpecialistFirmwareNamePriorityTests` include nomi compositi
Firmware/BIOS+GPU/WiFi/Audio/Software, nomi ordinari validi,
HTML con oltre 96 pacchetti e preview di file osservati
per basename nella cartella locale.

Restano aperti il confronto su Recoil 16 reale (#120/#136),
le firme CAT/Authenticode (#128/#283) e il ranking SetupAPI (#130).
