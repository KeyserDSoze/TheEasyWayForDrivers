# Preview INF: validazione completa di Manufacturer, Models e Needs (#503)

## Difetto e rischio

`InfDependencyPreview` usava ancora `String.Split(',')` in tre punti:
`[Manufacturer]`, voci `[Models.*]` e `Needs`. Il parser
`InfCommaFields.TrySplit` già usato in `InfModelEvidence` e
`CopyFiles` rifiuta, invece, quote bilanciate ma mal posizionate e
gestisce i delimitatori protetti da virgolette.

Un `Models` con `Install` valido ma alias PnP corrotto poteva ancora
essere seguito dalla preview delle dipendenze. Una lista
`Needs=Inner,"Bad"junk` poteva attraversare `Inner`, segnalando
un SYS/DLL come presente, malgrado l'intero record non attendibile.
Il risultato alimenta anche il piano locale opt-in dei controlli di
CAT membership, ma non può certificare né firma né installabilità.

## Intervento
- **Manufacturer**: analizza tutti i campi con `InfCommaFields`
  prima di autorizzare qualsiasi sezione Models; record malformato
  provoca warning e nessuna radice aggiunta.
- **Models**: l'intero record quoted deve essere parsabile prima di
  visitare `Install.NTamd64` o altri sottoinsiemi; l'alias successivo
  invalido non promuove una prima sezione `Install` legittima.
- **Needs**: prima valuta la lista completa fino al limite di
  80 nomi e ogni token `SafeToken`; se un elemento non è
  interpretabile, non visita *nessuna* sezione di quella direttiva.
  `Include` esterni rimangono review-only.
- Regressioni con NVIDIA/Killer/Realtek, doppie quote escaped,
  virgole quoted, continuazioni di riga, più Needs locali, sorgenti
  SYS/DLL in cartelle reali sintetiche e conferma che una direttiva
  `CopyFiles` indipendente resta analizzabile.

## Limiti

Preview read-only senza interprete SetupAPI completo, non
certifica completezza del package o verifica firma CAT/INF.
Budget INF 4 MiB, 32KiB/riga logica, 80 Needs, 12 livelli,
80 riferimenti, inventario filesystem e policy di sicurezza
immutati. QA reale #58/#128/#130/#136 e benchmark #91 ancora aperti.
