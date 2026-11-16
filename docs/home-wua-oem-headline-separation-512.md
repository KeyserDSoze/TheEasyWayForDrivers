# Home OmegaDrive: separare aggiornamenti WUA e segnalazioni OEM (#512)

## Bug
La Home usava `wuaCount + oemRecommended` per dichiarare
`N possibili aggiornamenti rilevati`. `wuaCount` include GUID
BrowseOnly, firmware e pass Advanced; `oemRecommended` arriva dai
risultati OEM dichiarati applicabili, non installabili da OmegaDrive.
Perfino quando tutti i pass WUA fallivano, due consigli OEM facevano
comparire «2 possibili aggiornamenti».

## Correzione
- Il numero **possibili aggiornamenti** in Home conta soltanto i
  GUID WUA ordinari restituiti da pass sorgente `Completed` e ammessi
  da `QuickUpdatePolicy.AllOrdinaryIds`, la stessa policy del
  percorso `Aggiorna tutto` (con ulteriore verifica all'installazione).
- WUA firmware/BIOS, BrowseOnly e Advanced rimangono **risultati
  Windows Update da verificare**, in clausola distinta.
- Le raccomandazioni OEM sono mostrate come **segnalazioni OEM da
  verificare**, mai sommate alle offerte WUA.
- Con WUA fallito il titolo dichiara sempre ricerca non completata
  anche se il produttore ha pubblicato dei consigli. Con WUA
  parziale si mantiene il prefisso di copertura parziale.
- Quando WUA è riuscito con zero offerte e vi sono suggerimenti
  OEM, la Home precisa «Nessun candidato Windows Update dalle fonti
  consultate»; non conclude che non esistano driver OEM.
- Nel sottotitolo è ribadito che i consigli OEM non entrano in
  `Aggiorna tutto`.

## Regressioni
`SimpleScanWuaOemHeadlineSeparationTests` e aspettative
aggiornate in `SimpleScanPresentationTests`,
`SimpleScanOrdinaryReviewSplitTests` e
`AutomaticOemCatalogCoverageTests`: fonti WUA 0/1/4,
WUA fallito, pass parziali, OEM 1/2/3, firmware/optional/advanced,
catalogo OEM fallito, software WUA informativo, niente conteggi
installabili inventati.

Non cambia l'elenco `DriverUpdateInfo`, le autorizzazioni WUA,
la verifica d'installazione, la sorgente OEM o la firma dei driver.
QA screenshot Windows (#114/#94/#85) resta da completare.
