# OmegaDrive — matching WUA generico per modello (solo revisione) — #208

## Problema e risultato

Nella v0.0.99 il collegamento dei driver Windows Update ai dispositivi
`DriverUpdateMatcher.MatchStrength` richiede un ID PnP **identico**.
Questo è corretto per decidere quali update mostrare sul pulsante del
dispositivo, ma può nascondere dalla diagnostica un aggiornamento già
trovato da WUA.

Esempio notebook con NVIDIA RTX 4060:

- Hardware ID del dispositivo:
  `PCI\\VEN_10DE&DEV_28A0&SUBSYS_16A217AA`
- ID restituito da WUA:
  `PCI\\VEN_10DE&DEV_28A0`
- Compatible ID dall'inventario WMI: **assente/non leggibile**.

Il candidato non è un match hardware esatto, però ha lo **stesso
VEN/DEV generico**. La nuova correlazione controlla quel modello
attraverso il validatore già usato per preparare la ricerca manuale
nel Microsoft Update Catalog, senza chiamare il sito o esportare dati.

## Semantica sicurezza

- **Match WUA esatto o Compatible ID già presente:** percorso
  precedente invariato. Se il candidato è ordinario e rispetta
  `QuickUpdatePolicy`, la relativa azione installazione del
  dispositivo rimane disponibile.
- **Solo modello pubblico generico**, senza match PnP completo:
  `ModelOnlyReview`, nuovo gruppo **separato** da `Ordinary`
  e `ReviewOnly`. In scheda dispositivo la dicitura informa
  `SUBSYS e compatibilità OEM NON verificati`.
  Non è un driver nuovo, non autorizza il pulsante di installazione
  del dispositivo e **non modifica la selezione globale** degli update
  già restituiti e gestiti autonomamente da Windows Update.
  Gli update ordinari WUA eventualmente presenti in `Aggiorna tutto`
  mantengono **le stesse regole esistenti**; la correlazione debole
  non aggiunge GUID né modifica l'installabilità globale.
- **Modello WUA obbligatoriamente generico:** `DriverHardwareID`
  o uno dei massimi 256 ID aggiuntivi letti da WUA deve coincidere
  con l'intero modello pubblico, senza `SUBSYS`, `REV` o suffissi.
  Il dispositivo deve avere un Hardware ID specifico riconoscibile.
  Non usare il nome, il produttore, solo `CompatibleID`,
  ID del dispositivo/istanza PnP, un prefisso o wildcard.
  Solo bus `PCI`, `USB`, `HDAUDIO`, `INTELAUDIO`, `SWC`.
- Escludere BIOS/UEFI/firmware, GUID invalidi e fonti non registrate
  tra i pass WUA conosciuti. La lista per dispositivo è limitata
  a **16** candidati review-only ordinati e deduplicati per GUID.
- Il fallback **non legge driver OEM online**, non modifica WUA,
  non scarica, non esegue EXE e non installa nulla. Gli ID
  completi e i seriali non vengono esportati né registrati:
  la comparazione avviene in memoria sul PC.

## UI

Dopo **Analizza il PC** (ricerca approfondita), scegli un dispositivo
in **Dispositivi**. La sezione WUA di revisione può mostrare
`Possibile corrispondenza al solo modello`. Se l'indizio è un
GUID WUA ordinario, il comando per aprire l'elenco seleziona il
filtro completo invece del solo filtro `Avanzati`. La presenza
nel filtro non significa compatibilità del notebook e non abilita
l'azione per il dispositivo.

## QA e limitazioni

I test sintetici coprono NVIDIA 4060, Intel/Killer, audio Realtek
e NVIDIA, SWC, USB, chipset modello diverso, SUBSYS differenti,
fonti non WUA, GUID falsi, firmware, WUA entry aggiuntive, 16-row
cap, duplicati fra pass, priorità del match esatto e stringhe PnP
malformate. Compilazione e test `net10.0`/WPF con gate Windows.

**Questo migliora la correlazione per dispositivo di WUA già
scansionato, NON aumenta per magia il numero di GUID disponibili
nel catalogo.** La differenza con Driver Easy e il test
sul PCSpecialist Recoil 16 restano aperti (#91, #136).
Per trovare veramente nuovi pacchetti servono fonti supportate
OEM per la configurazione reale, non endpoint NVIDIA interni
non documentati.

## v0.0.175 — stesso SUBSYS, sola REV omessa (#315)

È stato aggiunto un **secondo** caso di correlazione debole per
`IWindowsDriverUpdateEntry.DriverHardwareID` restituiti da WUA Type=Driver.
Se Windows descrive il notebook come
`PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA&REV_A1` e WUA dichiara
**esattamente** `PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA`,
OmegaDrive mostra il GUID come **indizio di revisione manuale**.
La regola è condivisa con il confronto locale degli INF OEM (#310).

- Sono supportati esclusivamente modelli primari validati sui bus
  PCI, HDAUDIO, INTELAUDIO e SWC con `SUBSYS_XXXXXXXX` (8 cifre
  esadecimali) seguito da un `REV_XX` terminale (2 cifre).
  Non si rimuove **mai** il SUBSYS; REV differenti o altre parti
  dell'ID non generano una correlazione.
- Il titolo, il nome commerciale, il provider, i soli Compatible ID e
  i PnP Instance ID non sono usati per costruire gli indizi. WUA deve
  aver restituito un GUID valido da una fonte ufficiale già interrogata
  e non essere un aggiornamento firmware.
- Lo stesso indizio è utilizzato in `DeviceWuaMatchGroups`, nelle
  otto categorie del rapporto di copertura e nell'audit aggregato
  WUA→PnP, con gli stessi limiti, privacy e dedup per GUID.
- Il risultato è nel gruppo `ModelOnlyReview`: **non** in `Ordinary`,
  non in `ReviewOnly`, non nel tasto Aggiorna per dispositivo o in una
  nuova offerta di Aggiorna tutto. Le associazioni WUA exact e Compatible
  confermate da Windows continuano ad avere precedenza.
- Nessuna nuova ricerca di cataloghi online viene eseguita. Le
  informazioni richiedono ancora verifica CAT, ranking, OS e test
  sul notebook Recoil16 (#91/#136).

*Nota:* la vecchia descrizione dei soli modelli pubblici rimane valida:
questa è una sottoclasse aggiuntiva di indizi, distinta dai match
hardware effettivi.
