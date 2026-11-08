# OmegaDrive — copertura NVIDIA, Intel/Killer e Realtek in Home (#230)

## Perché

La v0.0.109 mostra subito la lista dei driver realmente offerti da
Windows Update, ma nasconde in due expander i dettagli sui produttori.
Chi osserva due risultati Intel può credere che OmegaDrive non riconosca
NVIDIA o Realtek, oppure che l'assenza in WUA significhi «tutti aggiornati».

## Nuova UI

Nella **pagina risultati**, subito sotto il titolo e sopra la lista degli
aggiornamenti, compaiono tre card sintetiche:

- **NVIDIA** — GPU, audio e componenti classificabili tramite ID PnP/driver.
- **Intel / Killer** — dispositivi display/rete/Bluetooth dei due marchi,
  senza confondere root port e chipset bridge con driver aggiornabili.
- **Realtek** — soprattutto audio/network, anche con nome Windows generico
  quando è presente un ID codec riconoscibile.

Per ogni gruppo, **due righe distinte** indicano il numero di dispositivi
PnP classificati e il numero di candidati Windows Update **Type=Driver**
correlati mediante Hardware ID/Compatible ID. I GUID dello stesso candidato
sono deduplicati all'interno del gruppo, non sommati tra gruppi perché una
singola offerta WUA può riguardare più device.

Quando l'inventario PnP non è verificato, la UI dice «Inventario PnP non
verificato» e «Correlazione WUA non disponibile». Se nessuna ricerca WUA
riesce, dice «Ricerca WUA non verificata». Se sono fallite alcune fonti,
aggiunge **fonti Windows parziali** invece di affermare che non esistono
altri driver. Zero candidati significa zero **fra le fonti consultate**,
non un PC completamente aggiornato.

Il pulsante **Dettagli delle fonti** espande direttamente la sezione OEM
e la matrice diagnostica degli otto componenti Intel/Killer/NVIDIA già
esistente: non esegue download o interrogazioni esterne.

## Sicurezza, privacy, limiti

- Le card sono solo lettura e non hanno azioni di installazione.
- Non si aggiunge nulla alla lista `DriverUpdates` né ad `Aggiorna tutto`.
- Handoff OEM, notizie NVIDIA, `Type=Software` WUA e titoli di ricerche
  parziali NON sono conteggiati come candidati driver verificati.
- Nelle card appaiono soltanto nomi di gruppi prefissati e conteggi:
  mai nomi di PC, nomi dispositivo, Device Instance ID, Hardware ID,
  numero ordine o percorsi locali.
- La classificazione è euristica e dipende dai dati del sistema:
  «nessun dispositivo classificato» non prova assenza fisica.
- La nuova schermata non scarica/consulta automaticamente i cataloghi
  NVIDIA/Intel/Realtek, non certifica versioni OEM o installabilità.
- Il benchmark reale Recoil 16, 2 contro 8 driver, resta aperto
  (#91 e #136). La CI usa fixture sintetiche.

## Test di regressione

Le fixture coprono NVIDIA RTX con nome Windows generico, Killer Wi-Fi,
Realtek HD Audio, de-dup GUID WUA, ricerche fallite/parziali, inventario
non verificato, esclusione bridge chipset e indizi Type=Software, nonché
l'assenza di identificativi privati nei testi renderizzati.

Rif #95 #100 #114 #91 #136.
