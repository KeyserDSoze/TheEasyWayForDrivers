# Feed NVIDIA GeForce: selezione degli annunci dopo il filtro (#289)

## Difetto
Il lettore ufficiale RSS di NVIDIA e gia' limitato a 512 KiB e
vincolato a un solo URL HTTPS con consenso esplicito. Il parser
fermava l'analisi dopo **80 articoli generici** oppure dopo i primi
**quattro articoli candidati**, prima di ordinare per data. Questa
perdita era particolarmente visibile se notizie GeForce gaming/DLSS
precedevano un vero annuncio Game Ready/Studio Driver.

## Modifica
- Esamina ogni nodo `item` del **medesimo XML gia' bounded** (512 KiB,
  DTD/entita' esterne vietate), filtrando gli annunci editoriali driver.
- Prima di produrre le quattro righe UI, valida data (ultimi 90 giorni),
  titolo sanitizzato ASCII e link NVIDIA HTTPS in path
  `/en-us/geforce/news/`.
- Deduplica per URL privo di tracking e conserva la **data valida piu'
  recente** se il feed riporta lo stesso articolo piu' volte.
- Ordina per data decrescente e mostra soltanto quattro notizie; la
  quantita' massima di input e il timeout HTTP (12 s) non cambiano.
- Test: >100 articoli non driver prima dell'annuncio, cinque annunci
  fuori ordine, duplicati, link non attendibili, feed enorme/XXE,
  nessun driver dichiarato installabile.

## Limiti di prodotto
Questa fonte NVIDIA e' un **notiziario editoriale**: non verifica il
driver di una GeForce RTX 4060 Laptop, la versione installata, il
modello OEM o la compatibilita' di un pacchetto specifico. Nessun
pacchetto viene scaricato o installato, nessuna chiamata ad API
private NVIDIA, nessuna modifica WUA, firmware o `Aggiorna tutto`.
I task reali rimangono #22 (catalogo), #91 (gap 8/2), #136 (QA).
