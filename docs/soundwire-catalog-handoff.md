# OmegaDrive — ricerca SoundWire nel Catalogo Microsoft, privata e manuale (#184)

## Gap successivo a v0.0.87

La release v0.0.87 ha aggiunto a Discovery+ il riconoscimento INF locale dei bus
`SOUNDWIRE`, `SNDW` e `ACP`. Il pulsante già disponibile «Cerca nel
Catalogo Microsoft» non riconosceva però le famiglie SoundWire, e non poteva
aprire una ricerca anche quando Windows riportava ID generici pubblicati sul
Catalogo.

Microsoft Update Catalog documenta una voce con gli Hardware ID
`SOUNDWIRE\AGGREGATOR` e `SOUNDWIRE\AGGREGATEDSPEAKER`:
https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=6084d0de-4adc-4a58-a075-2a9d2b08eeb5

La ricerca nel Catalogo è **manuale**, in browser, e non equivale a ciò
che Windows Update Agent offre a un PC specifico.

## Ambito del cambiamento

- `MicrosoftUpdateCatalogSearch.Create` riconosce *esclusivamente* gli
  identificativi generici `SOUNDWIRE\AGGREGATOR` e
  `SOUNDWIRE\AGGREGATEDSPEAKER`, oltre alla famiglia strutturata
  `SOUNDWIRE\SDCA_##&MAN_####&FUNC_####` (hex). Un ID SDCA con campi
  `TYPE`, `VER`, `ADR`, `LID`, `UID`, `SUBSYS` viene **accettato
  solo** nella sequenza/grammatica ammessa e i campi vengono rimossi
  dalla query finale. La ricerca della famiglia SDCA è **best-effort**:
  nessuna certezza di pubblicazione o risposta del Catalogo.
- I token `SDCA`, `MAN` e `FUNC` sono usati come famiglia di modello,
  mentre numero di istanza, indirizzo, link, UID, SUBSYS, versione,
  revisione, nome utente e titolo locale **non sono inviati**.
- Se è disponibile un Hardware ID adatto, vince su ogni Compatible ID;
  se non lo è, un Compatible ID valido è ammesso ma segnalato come
  evidenza più debole.
- La Home e la scheda dispositivo riutilizzano lo stesso dialogo di
  consenso: l'utente vede il testo esatto della query prima di aprire
  il browser Microsoft `https://www.catalog.update.microsoft.com`.
- Ricerca di classe SoundWire aggregata esplicitamente descritta come
  **generica** e non come dispositivo preciso.

## Limitazioni di sicurezza e copertura

Un risultato del Catalogo non attesta firma CAT, applicabilità Windows,
compatibilità OEM/CHID/SUBSYS, ranking PnP, data/versione o necessità.
OmegaDrive non legge né scarica automaticamente il Catalogo, non installa
pacchetti né aggiunge nuove righe a `Aggiorna tutto`.

`SNDW` e `ACP` rimangono supportati dal **confronto INF locale**
ma **non** dal collegamento al Catalogo: senza un formato pubblico
affidabile per ridurre gli ID ai soli token ricercabili e non privati
l'app preferisce non produrre query arbitrarie.

## Regressione e QA

I test xUnit coprono ID generici, famiglia SDCA, upper/lowercase, stripping
`TYPE`/`VER`/`ADR`/`LID`/`UID`/`SUBSYS`, priorità ID,
fallback Compatible ID, host HTTPS, input deformati e assenza di ogni
conferma di installabilità. Un test sintetico di URL non prova risultati
su hardware.

QA hardware residuo: eseguire «Analizza il PC» su un Windows con SoundWire,
aprire la ricerca previa conferma, confrontare il modello reale, la
compatibilità OEM e la firma del pacchetto nel Catalogo. Non pubblicare
Device Instance ID, seriali o numeri ordine in issue pubbliche.
Rif. #91, #100, #136, #172, #182, #184.
