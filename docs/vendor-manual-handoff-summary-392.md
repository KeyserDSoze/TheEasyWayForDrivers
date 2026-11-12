# Pannello OEM: fonti manuali raggruppate, driver realmente valutati separati (#392)

## Problema

Prima una scansione con 100 dispositivi Intel/NVIDIA/Realtek poteva mostrare fino a 100 righe quasi identiche che portavano allo stesso sito ufficiale. Sono soltanto rimandi manuali: il relativo conto non rappresenta driver nuovi, presenti o installabili.

## Soluzione

- `VendorSourceDiscovery.Find` rimane invariato e continua a rilevare fino a 100 dispositivi tramite modelli PnP e associazioni WUA. I report per dispositivo e i test di discovery mantengono questa evidenza.
- Solo `MainWindow.RefreshVendorChecks` applica `VendorManualHandoffSummary.Create` per ottenere una riga per coppia **produttore + URL ufficiale fisso**. I collegamenti Intel Wi-Fi/Killer, Bluetooth e Driver & Support Assistant restano distinti.
- La vista mostra quanti dispositivi sono stati riconosciuti **nell'anteprima campionata** e quanti hanno almeno una corrispondenza WUA per ID. Non equivale a driver OEM verificati o versioni disponibili. Contenuto dei campi derivato solo da etichette fisse, non da PnP serial, nome macchina, provider arbitrari o URL con query.
- Il riepilogo è un pannello di supporto manuale: nessun pulsante aggiorna/installare cambia destinazione. Gli aggiornamenti OEM segnalati necessari da Lenovo/Dell/HP e le notizie NVIDIA continuano ad apparire come righe separate e non vengono riassunti.
- Sono ammesse soltanto le destinazioni HTTPS già codificate e il vendor ufficiale corrispondente. Il limite di ingresso rimane 150, quello di discovery predefinito 100; nessuna nuova fonte, connessione o processo è avviata.

## Test e QA

Regressioni con 140 dispositivi Intel e NVIDIA GPU/audio, Killer e Realtek, conteggio gruppo, link distinti Intel, correlazione WUA conteggiata per dispositivo e non come driver installabile, URL spoofed/query/credenziali, produttori falsi e riservatezza. La CI Windows verifica anche la struttura WPF.

Restano aperte la riprogettazione completa post-scan #114/#94, la misurazione hardware Recoil 16 #91/#136 e la firma Authenticode #283. Questa modifica non trova nuovi driver né prova installabilità.