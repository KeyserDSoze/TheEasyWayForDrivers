# HP Image Assistant: redazione privacy e dedup SoftPaq (#532)

## Rischio
HPIA valuta il PC corrente e i campi XML di raccomandazione possono
contenere testo locale sensibile. La protezione di #445 non copriva
marcatori `ORDER_`, `CUSTOMER_`, `SERIAL_` o `SECRET_`, né
marcatori spezzati da Unicode invisibile. La copia dei dettagli
poteva quindi riportare stringhe identificative, anche dopo la
soglia di anteprima di 160 caratteri.

Il primo record con un SoftPaq valido bloccava inoltre tutti i
duplicati, inclusi titoli successivi pubblici quando il primo
era stato oscurato.

## Modifica
- `ContainsPrivateReference` esamina **l'intero campo originale**
  in una copia di ispezione priva di caratteri di controllo, formato
  e direzionali invisibili. Oscura l'intero campo se trova marcatori
  riservati, percorsi, URL, email o token PnP.
- `ToVendorChecks` usa la stessa protezione anche sui Package
  ricevuti direttamente da codice legacy/IPC.
- A parità di `Softpaq.Id` (case insensitive), se il primo titolo
  è `[dato riservato]`, un successivo titolo pubblico valido
  sostituisce **l'intera** raccomandazione, non soltanto il nome.
  Se il titolo iniziale è già pubblico, resta la prima voce.
- Test di XML sintetici per token normali/invisibili/oltre cap,
  percorsi diretti e duplicati HP.

L'esito HPIA resta **evidenza advisory HP**, non prova di firma
CAT/INF, di disponibilità WUA o autorizzazione all'installazione.
QA reale su hardware HP resta in #108/#85.
