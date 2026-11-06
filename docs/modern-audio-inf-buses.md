# OmegaDrive — bus audio moderni SOUNDWIRE / SNDW / ACP (issue #182)

## Perché

La allowlist di `InfModelEvidence` (utile a confrontare pacchetti ZIP/CAB e
cartelle OEM **locali**) non includeva `SOUNDWIRE\\`, `SNDW\\` e
`ACP\\`. Un file INF poteva quindi dichiarare un Hardware ID esatto
del PC senza che OmegaDrive lo mostrasse nel confronto: un falso «zero
corrispondenze» nel controllo di pacchetti, **non** nella ricerca WUA.

Un esempio ufficiale di ID è `SOUNDWIRE\\AGGREGATOR` e
`SOUNDWIRE\\AGGREGATEDSPEAKER`:
[Microsoft Update Catalog — SoundWire Audio](https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=6084d0de-4adc-4a58-a075-2a9d2b08eeb5).
Inoltre, identificativi `SNDW\\CLASS_...` e
`ACP\\DEVTYPE_...` possono apparire in pacchetti audio di notebook:
l'estensione serve a leggere **le dichiarazioni INF e gli ID del PC**,
senza attribuire automaticamente un OEM a tali bus.

## Modifiche

- Aggiunti `SOUNDWIRE`, `SNDW`, `ACP` alla lista chiusa di
  enumeratori ammessi dalla regex del parser INF; grammatica e limiti
  di lunghezza invariati, nessuna ricerca fuzzy o per nome.
- `HardwareIdentityParser.Describe` etichetta i tre bus senza dedurre
  un produttore o la disponibilità di un driver.
- Test per anteprima INF, confronto PnP mirato, matching exact/alias,
  preflight architettura/build Windows, ZIP e cartella già estratta.
  ID differenti, prefissi simili, path traversal e spazi sono rifiutati.
- Il confronto read-only continua a non installare/eseguire/stageare file,
  a non scaricare cataloghi e a non promuovere un ID compatibile a
  Hardware ID primario. `IsInstallable` e `IsVerifiedCompatible` restano
  **false**. Firma CAT, ranking SetupAPI e OEM rimangono verifiche distinte.

## Come misurare il risultato su Windows reale

1. Confrontare la stessa cartella/ZIP di driver ufficiali, già disponibili
   localmente, prima e dopo la patch.
2. Su dispositivi che hanno `SOUNDWIRE`, `SNDW` o `ACP` nel loro Hardware
   ID controllare se crescono gli **INF con corrispondenza**, non gli update
   dichiarati come installabili.
3. Verificare che dispositivi con ID diversi e i pacchetti non verificati
   rimangano fuori da `Aggiorna tutto`; non condividere identificativi
   d'istanza o numeri di ordine nei report pubblici.
4. Ripetere i controlli con audio NVIDIA/Intel/Realtek e formato ZIP/CAB/
   cartella sul portatile di prova.

## Limiti

La modifica **non** aggiunge una fonte di driver NVIDIA/Realtek/AMD,
non interroga Microsoft Update Catalog in automatico e non promette
nuovi candidati WUA. Il divario reale «8 Driver Easy vs 2 WUA» (#91)
richiede ancora QA hardware e sorgenti OEM per-device. La build e i
test Windows sono controllati dalla CI della PR; il test sintetico
non sostituisce QA su driver OEM reali.

Rif. #91, #100, #136, #164, #182.
