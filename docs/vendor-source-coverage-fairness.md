# OmegaDrive — copertura fonti OEM senza starvation e nuovi bus PnP (#151, #152)

## Difetto risolto
`VendorSourceDiscovery.Find` limita le fonti OEM a **100 dispositivi** (massimo configurabile 150). Prima ordinava l'inventario per produttore e applicava il limite: 100+ dispositivi Intel potevano saturare il risultato, facendo sparire le fonti NVIDIA, Realtek o Qualcomm anche se il dispositivo era rilevato da Windows.

Ora la selezione è distribuita **round-robin tra vendor** usando lo stesso budget, senza aumentare le dimensioni della UI:
- Un dispositivo con corrispondenza WUA **effettiva per Hardware ID/Compatible ID** viene prioritizzato all'interno del proprio produttore.
- A parità di corrispondenze vengono privilegiati i dispositivi con problemi Windows.
- Se il budget è più piccolo del numero di vendor, hanno priorità i gruppi con corrispondenze WUA o errori, poi l'ordine alfabetico.
- La selezione è deterministica, senza duplicare istanze PnP. Lo stesso Update ID WUA restituito da più pass conta una volta sola.
- Il limite 0–150 è invariato. Se il limite è inferiore al numero di vendor, non tutti i gruppi possono essere visualizzati: questo non dimostra che gli altri non esistano.

**Questo migliora la copertura della diagnostica delle fonti**, non la quantità di aggiornamenti WUA effettivamente offerti dal servizio Windows.

## Nuovi dispositivi rilevati con nomi generici
Da identificativi realmente presenti nel catalogo Microsoft Update:
- **Realtek su Intel Smart Sound (SST)**: `INTELAUDIO\\FUNC_01&VEN_10EC`, oltre al preesistente `HDAUDIO\\FUNC_01&VEN_10EC`. Il codec è classificato anche quando Windows riporta una classe PnP sconosciuta, perché l'ID audio specifica `FUNC_01`.
- **Qualcomm/Atheros Bluetooth**: `USB\\VID_0CF3`, solo quando la classe o il nome indicano rete/Bluetooth.

Il controllo considera Hardware IDs e Compatible IDs e usa prefissi **delimitati**, quindi `VEN_10EC0` o `VID_0CF30` non corrispondono. Camera USB e bridge senza una categoria pertinente restano esclusi. `HardwareIdentityParser` visualizza `INTELAUDIO` come bus Intel Smart Sound senza dedurre il produttore del codec dal nome del bus.

Fonti ufficiali di esempio (solo conferma dell'esistenza dell'hardware, **non** raccomandazioni per il PC attuale):
- https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=5a9494dc-8bab-4e2a-bbf5-197b990cf066
- https://catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=a520d357-55af-4485-8fa1-4c90a3286650

## Sicurezza e confini
Queste righe sono **handoff a fonti ufficiali**; `IsVendorApplicable = false`, `WuaMatches` esprime solo candidati Windows Update realmente associati per ID. Nessuna richiesta a API vendor non documentate, nessun catalogo OEM interrogato da remoto, nessun download o driver reso installabile in `Aggiorna tutto`. `QuickUpdatePolicy` rimane invariata. Le destinazioni web sono allowlist HTTPS già esistenti.

## Validazione
- [x] Test sintetici: 180 Intel + NVIDIA RTX GPU/HD Audio/Platform + Realtek + Qualcomm con limite 100.
- [x] Priorità WUA, dedup Update ID, limiti 0/1/2; nessun package OEM inventato.
- [x] Nuovi codec SST Realtek, Bluetooth Qualcomm USB, Compatible IDs, falsi prefissi e webcam esclusi.
- [x] Bus INTELAUDIO e regressioni su identificativi Windows.
- [ ] CI Windows .NET 10: IPC, WPF, build, test.
- [ ] QA reale #91/#136 su Recoil 16, confronto 2 vs 8: solo dati locali/aggregati, mai seriali o Hardware IDs nei report condivisi.
- [ ] QA visuale e accessibilità su Windows reale #95.

Questa PR **non** dimostra che siano comparsi nuovi aggiornamenti NVIDIA: se WUA non li offre, il canale ufficiale OEM va ancora verificato.
