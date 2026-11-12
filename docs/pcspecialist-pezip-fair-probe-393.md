# PCSpecialist: selezione equa dei PE-ZIP EXE nell'HTML completo (#393)

## Bug

OmegaDrive v0.0.255 manteneva fino a 2.048 nomi di pacchetti HTML già validati, ma il pianificatore di EXE riceveva soltanto i 96 nomi iniziali della UI. Inoltre ordinava alfabeticamente i file preferiti. Con 25 EXE chipset Intel, un pacchetto NVIDIA RTX o Intel Killer presente sul disco ed elencato nel portale poteva rimanere fuori dai 24 probe PE-ZIP.

## Correzione

- Il confronto locale usa tutti i nomi HTML sanitizzati (massimo 2.048), conservati solo in RAM. Nessun numero ordine, token, link o HTML grezzo passato al planner.
- I file EXE con basename che coincide esattamente con un nome HTML ricevono turni equi tra le categorie già usate per la preview (chipset, NVIDIA GPU, Wi-Fi/Killer, Realtek audio, ecc.). L'ordine dentro ogni categoria è deterministico; senza HTML il vecchio ordinamento globale alfabetico resta invariato.
- Il budget resta 24 probe EXE, 24 sorgenti analizzate, 3.000 file visitati e 4 GiB totali. Gli EXE non probati o proprietari sono conteggiati come non esaminati: copertura parziale, mai zero driver dedotti.
- I file PE vengono letti senza eseguirli; un eventuale archivio ZIP statico allegato viene letto secondo i vincoli dell'ispettore OEM. Gli ID PnP degli INF non provano firme CAT, necessità di aggiornare o installabilità.

## Regressioni

HTML sintetico con 130 nomi chipset, 130 NVIDIA e un Killer; cartella con 25 EXE chipset proprietari, NVIDIA GPU e Killer in due EXE contenenti ZIP+INF reali. La vecchia preview da 96 nomi non li raggiunge, mentre la nuova priorità full-catalog e la selezione equa li esaminano entro i 24 probe. I due INF vengono correlati esattamente a dispositivi NVIDIA/Killer sintetici, senza eseguire installer. Verificati inoltre limiti, nomi esatti, privacy e modalità senza HTML.

QA hardware reale Recoil 16 (#91/#136), QA Windows end-to-end (#85) e firma Authenticode (#283) restano aperti.