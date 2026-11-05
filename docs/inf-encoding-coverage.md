# INF OEM: codifiche, dispositivi riconosciuti e copertura (#126)

## Problema riscontrato nel codice v0.0.60

I pacchetti PCSpecialist/NVIDIA/Intel/Killer possono contenere file
INF **ANSI** o **UTF-16 big-endian**, entrambi riconosciuti da
Windows. OmegaDrive invece leggeva i file come UTF-8 o UTF-16
little-endian: un INF ANSI contenente un carattere localizzato
poteva essere ignorato anche con Hardware ID, versione e sezioni
Models perfettamente validi.

Microsoft indica ANSI o UTF-16 per INF; raccomanda UTF-16 LE e per
le versioni localizzate raccomanda Unicode:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/display/general-unicode-requirement
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/creating-international-inf-files
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/general-guidelines-for-inf-files

## Codifica condivisa nei confronti locali

La stessa classe **OemInfTextDecoder** decodifica gli INF sia per
l'inventario dei file dichiarati sia per il successivo controllo
di applicabilità preliminare. Viene quindi utilizzata da:

1. **ZIP** già scaricati dal produttore;
2. **CAB** letti tramite la fase INF-only basata su expand.exe;
3. **cartelle OEM** già disponibili, senza eseguire installer.

Gestisce:
- UTF-16 LE con BOM;
- UTF-16 BE con BOM;
- UTF-8 con BOM e UTF-8 senza BOM, solo per **evidenze read-only**
  (non prova che SetupAPI possa installare un INF UTF-8);
- ANSI legacy senza BOM: **recupero conservativo della sola sintassi
  ASCII** (sezioni, Hardware ID, decorazioni, DriverVer). I byte
  non ASCII sono sostituiti con segnaposto e sono ammessi solo
  nei **commenti** o nei valori della sezione [Strings] (non in
  nomi di sezioni, ID o direttive di installazione). Non viene
  indovinata la codepage Windows CP1252/ACP del file.

## Blocco dei contenuti malformati

- Dimensione di ogni INF <= 1 MiB, indipendentemente dal formato.
- Unicode con surrogate malformati, UTF-32/BOM sconosciuti,
  NUL, caratteri C0/DEL o di controllo bidi: **file scartato**.
- Nessun fallback ANSI per riparare stringhe non ASCII in
  sezioni Hardware ID, [Manufacturer], [Models] o direttive:
  qualsiasi ambiguità che potrebbe generare un match falso
  viene rifiutata.
- Fallback ANSI ammesso solo con [Version] riconoscibile e
  contenuto prevalentemente ASCII. Non vengono tentati
  encoding casuali o eseguiti parser esterni.
- Il fatto che un INF sia decodificato **non** prova firma CAT,
  provenienza OEM, compatibilità SetupAPI o necessità di
  installare la versione proposta.

## Report leggibile

Invece di mostrare soltanto "zero aggiornamenti" quando
alcuni file sono illeggibili, il report del pacchetto OEM mostra:
- **INF presenti**;
- **INF leggibili**;
- **INF scartati**;
- **INF con ID PnP dichiarati**;
- **INF con sintassi ANSI recuperata**;
- dispositivi PnP correlati e possibili versioni superiori
  **da verificare**.

Se anche un solo INF è scartato, il confronto viene
esplicitamente indicato come **parziale**. Non si registrano
percorsi privati, numero d'ordine PCSpecialist, seriali o
Hardware/Instance ID nel report aggregato copiabile.

Le evidenze OEM rimangono nella sezione **non installabile**:
nessuna variazione alla policy Windows Update o ad
**Aggiorna tutto**.

## Test automatici

- [x] Unicode LE e BE con BOM, recupero NVIDIA GPU, HDAUDIO,
  Killer/Intel e versioni INF.
- [x] UTF-8 con/senza BOM, lettura solo di evidenza.
- [x] ANSI con accenti in commenti e [Strings], ma ID
  e sintassi recuperati senza codifica locale indovinata.
- [x] Byte non UTF-8 nell'Hardware ID o nome di sezione:
  **rifiuto**, senza match inventato.
- [x] UTF-16 malformato, UTF-32, stringhe binarie/NUL,
  bidi, controllo C0, input oltre 1 MiB: **rifiuto**.
- [x] ZIP con INF misti (BE/LE/ANSI/broken):
  conteggi corretti per file/ID, risultati NVIDIA/Intel,
  uno scartato e zero installabili.
- [x] Cartella OEM con BE/ANSI e con INF corrotto:
  conteggi corretti, nessuna installazione.
- [x] **CAB Windows reale** creato con makecab.exe,
  estratto con expand.exe: INF BE o ANSI analizzato
  con la stessa logica.
- [ ] Compilazione/test su CI Windows della nuova PR.
- [ ] QA con veri pacchetti PCSpecialist Recoil 16 e
  differenze misurabili prima/dopo (issue #91).

## Prossimi controlli reali

1. Scaricare ZIP/CAB dal sito ufficiale per l'ordine del
   proprietario, oppure usare cartelle OEM già presenti.
2. Confrontare con OmegaDrive v0.0.60 e la nuova release:
   file INF totali, file leggibili e corrispondenze NVIDIA
   RTX 4060 Laptop, HD Audio, Platform Controllers,
   Intel/Killer e Bluetooth.
3. Controllare perché eventuali INF siano scartati:
   codifica, mancanza di PnP ID, OS non corrispondente,
   versione non disponibile o errore di file.
4. Verificare gli aggiornamenti reali con versione,
   provenienza, firma e ranking SetupAPI; **non**
   confrontare solo il numero degli elementi proposti
   da Driver Easy.

**Limite:** questa correzione riduce i falsi negativi
dell'analisi degli INF OEM locali; **non** aggiunge un catalogo
NVIDIA/Intel online interrogabile automaticamente, né
garantisce un aumento dei driver installabili sul PC.
