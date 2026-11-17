# HP CMSL: redazione marcatori privati nei metadati e SoftPaq ID (#540)

## Difetto
Il parser dei metadati HP CMSL oscurava percorsi, email e identificativi
PnP ma non riconosceva alcuni riferimenti relativi al cliente o all'ordine
(`CUSTOMER_`, `ORDER_`, `SERIAL_`, `SECRET_`). Inoltre il SoftPaq ID,
se costituito da soli caratteri ASCII consentiti, poteva incorporare
questi stessi marker ed essere mostrato come identità pubblica.

## Intervento
- `HpSoftpaqCatalogParser.ContainsPrivateReference` controlla l'**intero**
  campo prima del limite UI di 150 caratteri e rileva i token anche se
  separati da caratteri di controllo/formato Unicode invisibili.
- I marker privati sono condivisi da titolo, versione, tipo di release,
  data e ID; l'ID che li contiene viene **rifiutato** senza troncamento,
  mentre un campo testuale viene redatto come `[dato riservato]`.
- La stessa sanitizzazione vale per JSON e `ValidateEntry`, quindi per
  Discovery+ e la valutazione OEM che utilizzano gli entry pubblici.
- Test sintetici di regressione per marker normali/invisibili/oltre
  limite, identità opache, dedup e preservazione delle categorie valide.

## Ambito
I SoftPaq CMSL sono **metadati HP**, non attestazioni di necessità
di aggiornamento o compatibilità di un singolo hardware. Non si
eseguono download/installazioni né si verificano firme CAT/INF.
Le policy per marker non equivalgono ad anonimizzazione garantita:
la verifica su hardware HP reale resta in #106/#85 e la copertura
Recoil 16 in #91/#136.
