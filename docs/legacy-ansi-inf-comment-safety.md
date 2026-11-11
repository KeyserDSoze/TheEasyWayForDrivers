# OmegaDrive — parser ANSI INF: commenti fuori da %...% e virgolette (#352)

## Difetto confermato nel codice v0.0.211

I file INF dei produttori Windows possono usare una codifica ANSI locale:
il decoder `OemInfTextDecoder` non può dedurre una code page in modo
affidabile e recupera solo **identificativi e sintassi ASCII**.
Le lettere ANSI non ASCII sono consentite esclusivamente nei commenti
veri e nei valori localizzati della sezione `[Strings]`;
vengono rese `U+FFFD` per la sola ispezione.

La validazione ANSI usava `line.IndexOf(';')` per tagliare il
commento, mentre il reader logico `InfLogicalLines` sa che `;`
all'interno di una stringa `"..." ` o un token `%...%` non indica
il commento. Si potevano quindi nascondere byte non ASCII nel campo
Hardware ID *dopo* un punto e virgola in `%Model;Name%` e far accettare
l'INF, benché l'alias hardware dichiarato contenesse byte legacy
ambigui. Il file poteva comunque mostrare un altro modello NVIDIA
valido nella stessa direttiva, portando a evidenza OEM parziale
che il controllo di sicurezza avrebbe dovuto scartare.

## Correzione

`IsSafeLegacyInf` usa ora un piccolo scanner **sui byte originali**,
prima di sostituire i byte estesi, con due stati:
- all'interno di una stringa racchiusa da virgolette;
- all'interno del token INF `%StringKey%`.

Solo `;` incontrati fuori da entrambi gli stati delimitano
un commento, come nel reader logico. I caratteri non ASCII
nella parte di sintassi `[Version]`, `[Manufacturer]`,
`[Models.NTamd64]` e altre direttive vengono respinti;
i byte estesi nei commenti veri e nei valori `[Strings]`
continuano a essere accettati senza inventare una conversione
CP-1252/locale.

Nessun nuovo parser INF è autorizzato all'installazione.
InfModelEvidence e InfPlatformPreflight restano analisi
read-only: un match del modello non sostituisce la verifica di
Windows, l'architettura, la versione OS, CAT/Authenticode o il
ranking di SetupAPI.

## Regressioni

Test byte-oriented su:
- NVIDIA e Intel Killer con alias corrotto dopo `%Model;Variant%`;
- semicolon dentro stringa quotata e sezione Manufacturer;
- `[Strings]` localizzate con semicolon interno e byte ANSI;
- commenti veri con byte ANSI e INF validi NVIDIA/Killer/Realtek;
- ZIP misto: pacchetto NVIDIA corrotto scartato, Realtek valido
  ancora riconosciuto e nessun percorso ordine/istanza esportato.

La compatibilità con i pacchetti PCSpecialist resta da verificare
su archivi reali selezionati dall'utente (#120/#124/#126).
Non si tratta di un nuovo catalogo driver e non prova
la soluzione del divario **8 vs 2** (#91/#136).
