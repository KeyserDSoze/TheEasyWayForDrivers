# Diagnostica aggregata WUA → PnP (issue #244)

## Problema che chiarisce

Il PC di riferimento Recoil 16 ha mostrato **due candidati** in
OmegaDrive, contro **otto righe** del programma Driver Easy (#91).
Questi numeri provengono da sorgenti e definizioni diverse:
un risultato Type=Driver confermato da Windows Update non è
equivalente a un pacchetto OEM elencato altrove.

L'inventario PnP è stato migliorato progressivamente per recuperare
ID NVIDIA/Killer/Realtek, ma senza una distribuzione *per GUID WUA*
non sappiamo se il limite dipenda dalla **quantità di aggiornamenti
effettivamente restituiti da Windows**, oppure dalla **mancata
associazione di ID a hardware rilevato**.

## Nuova diagnostica nel pulsante «Copia copertura»

La riga **«Diagnostica WUA→PnP (per GUID Type=Driver)»** compare nel
report aggregato della scansione. Conteggia **un GUID una volta**
(anche se presente in più pass WUA completati e dichiarato per più
dispositivi) e lo colloca in una sola categoria, per priorità:

1. **Hardware ID esatto**: almeno un dispositivo PnP ha uno degli
   identificativi dichiarati dal driver WUA nel suo elenco HardwareIds.
2. **Solo Compatible ID**: nessun Hardware ID, ma almeno un CompatibleId
   corrisponde.
3. **Solo modello generico**: corrispondenza debole su token pubblico
   PCI/USB/HDAUDIO/INTELAUDIO/SWC, **solo informativa**, *mai*
   autorizzazione ad aggiornare, quando un PnP più specifico è noto.
4. **Non attribuito**: il GUID Type=Driver è stato restituito
   correttamente, ma non ha una correlazione verificata con i
   dispositivi disponibili in inventario.

In più sono presenti due conteggi **trasversali**, da non sommare alle
quattro categorie: GUID **senza alcun Hardware ID dichiarato** e GUID
con **lista di associazioni WUA incompleta** (cap o getter COM guasti).

I titoli Type=Software e gli indizi delle ricerche WUA parziali sono
esclusi. Se WUA non è stata interrogata correttamente il testo è
**«ricerca Type=Driver non verificata»**; se manca l'inventario PnP,
le associazioni sono **«non valutabili»** senza falsi zeri.
Se una delle ricerche WUA è fallita o parziale, il testo riporta
che la copertura è limitata alle fonti confermate.

Il report è **anonimo**: soltanto etichette predefinite e numeri;
nessun titolo di update, GUID, versioni di driver, Hardware/Compatible
ID, Device Instance ID, cartelle, seriali, numero ordine o nome PC.
L'analisi rifiuta input oltre 2.048 dispositivi o 4.000 update
per evitare inferenze su collezioni incomplete o costi illimitati.

## Interpretazione dei risultati

- **2 GUID WUA in totale, entrambi associati**: il limite è nella
  *copertura delle offerte WUA*, non nel matching. Prossimo controllo:
  fonti OEM ufficiali e relativa applicabilità, non «driver
  mancanti» o installazioni arbitrarie.
- **Molti GUID WUA ma molti non attribuiti**: approfondire
  HardwareIds, CompatibleIds e le categorie senza PnP leggibile
  *localmente*, senza condividere identificativi privati.
- **Solo modello generico**: non elevare a update installabile;
  il modello non certifica SUBSYS/OEM/rank SetupAPI.
- **Lista WUA incompleta**: l'assenza di una corrispondenza
  non permette conclusioni sul singolo device.

Non vengono introdotti nuovi motori di catalogo, downloader,
staging, esecuzioni o installazioni OEM, e non viene modificata
`QuickUpdatePolicy` o `Aggiorna tutto`.

## QA

Test sintetici: NVIDIA GPU, Killer Wi-Fi, Realtek, quattro
categorie per GUID, GUID duplicati fra pass WUA con ID differenti,
fonti parziali/fallite, inventario non disponibile, titoli Type=Software,
input oltre cap e assenza di identificativi nei report.

Restano da verificare su Recoil 16 i risultati reali del report
e gli otto candidati elencati da Driver Easy (#91, #136).
