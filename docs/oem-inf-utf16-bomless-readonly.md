# INF OEM UTF-16 senza BOM — riconoscimento conservativo (#250)

## Difetto corretto

Fino alla v0.0.119 OmegaDrive accettava file INF UTF-8 (con/senza
BOM), UTF-16LE/BE **con BOM**, oppure ANSI legacy con grammatica
ASCII leggibile. Un INF UTF-16LE/BE **senza BOM** veniva tentato
come UTF-8: i NUL interleavati alla grammatica portavano
al rifiuto, anche quando sezioni `[Version]`,
`[Manufacturer]`, `[Models.NTamd64]` e Hardware ID
NVIDIA/Killer/Realtek erano perfettamente leggibili.

## Comportamento

`OemInfTextDecoder.Decode` riconosce adesso, solo in modalità
**analisi read-only**, le nuove codifiche
`Utf16LittleEndianNoBom` e `Utf16BigEndianNoBom`.

Non tenta una decodifica indiscriminata di contenuti binari:
richiede **lunghezza pari e almeno 64 byte**, una quota significativa
di zeri alternati nella corsia giusta, pochi zeri nella corsia
opposta, decodifica Unicode strict e presenza congiunta di
**`[Version]`, `[Manufacturer]` e `Signature`**.
L'assenza di questi segnali non prova che il file non sia
un vero INF: indica soltanto **codifica non identificabile
con affidabilità**. In tal caso il file viene scartato
come in precedenza, non convertito per approssimazione.

Restano obbligatori i controlli contro UTF-32, surrogati
invalidi, NUL residui, caratteri di controllo, bidi, e
limitazioni di dimensione **4 MiB**; il parser Models ha
le proprie regole di sezione, ID e architettura.
Nessun cambio all'ispezione del singolo ZIP (96 INF/24 MiB),
cartella OEM (384 INF/96 MiB), cataloghi CAT/WHQL
o alla lista aggiornamenti Windows.

## Cosa migliora

Il percorso ZIP/carta OEM/CAB che riutilizza il decoder
può associare in sola lettura ID PnP presenti negli INF
UTF-16 senza BOM invece di rifiutarli anticipatamente.
Il match resta una **evidenza di modello dichiarato**,
non un driver più recente, installabile o firmato.

Fixture testate: INF UTF-16LE e BE senza BOM contenenti
NVIDIA GPU, Intel/Killer Wi-Fi e Realtek HDAUDIO,
correlazione ZIP e confronto versioni, sintassi
obbligatoria assente, UTF-16 con surrogati invalidi,
bidi, lunghezza dispari e byte binari, oltre ai test
preesistenti ANSI/UTF-8/UTF-16 con BOM.

La verifica su pacchetti reali del Recoil 16 e il
benchmark **2 contro 8** restano aperti (#91, #126, #136).
