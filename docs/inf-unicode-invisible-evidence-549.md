# Decoder INF: caratteri Unicode invisibili (issue #549)

## Problema
Il decoder INF dei pacchetti ZIP/CAB e delle cartelle OEM rifiutava
NUL e specifici controlli di direzione, ma accettava U+200B (spazio a
larghezza zero), U+200E, U+2060 e altri caratteri Unicode Format, separatori
di linea/paragrafo Unicode e U+FFFD codificato intenzionalmente in UTF-8
o UTF-16. Sono caratteri ambigui in un file di sintassi Windows INF.

## Correzione
- Il decoder rifiuta dopo la decodifica i caratteri Unicode Format,
  LineSeparator e ParagraphSeparator oltre ai controlli C0.
- In UTF-8 e UTF-16 è rifiutato U+FFFD esplicito.
- **Eccezione necessaria:** solo il fallback ANSI legacy ASCII-only può
  generare U+FFFD per byte con il bit alto nei commenti e nei valori
  [Strings]. La codepage non viene indovinata, gli ID PnP restano ASCII,
  gli ID con byte non ASCII continuano a essere rifiutati.
- I caratteri accentati visibili in UTF-8 UTF-16 sono conservati.
- Il percorso ZIP segnala il file non interpretabile come scartato;
  non stampa percorsi, Hardware ID o numeri di ordine nell'audit.

## Limiti
La lettura è *read-only* e non conferisce attendibilità crittografica,
compatibilità con un modello o permesso di installare un driver. L'analisi
reale su INF OEM forniti dal PCSpecialist e i confronti driver rimangono
nelle #126, #136 e #91. E2E Windows rimane nella #85.
