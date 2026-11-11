# Ranking SetupAPI: data INF come spareggio del rank (#368)

## Perché

I driver già presenti nel Windows Driver Store sono classificati da
SetupAPI usando rank nativo. Nella diagnostica di OmegaDrive un pareggio
veniva mostrato per ordine **alfabetico di provider/descrizione**, e
solo dopo per versione. Windows, invece, per gli stessi rank sceglie
**prima la data INF DriverVer più recente, poi la versione maggiore**.

Riferimento: https://learn.microsoft.com/windows-hardware/drivers/install/overview-of-the-driver-selection-process

## Implementazione read-only

\`WindowsSetupApiRankReader\` inoltra adesso il \`DriverDate\` FILETIME
dell'\`SP_DRVINFO_DATA\` nativa a
\`WindowsSetupApiRankEvidence.FromNativeFileTime\`. Un FILETIME assente,
negativo o fuori range produce un campo nullable sconosciuto, mai una
data inventata. I dati visualizzati sono il **giorno UTC** e la
versione originale Windows, senza percorsi, seriali o hardware ID.

Ordinamento diagnostico: rank numerico crescente; a parità,
DriverVer date decrescente; poi versione decrescente; solo infine
provider/descrizione alfabetici. Con rank pari e una data non
verificata, il pannello visualizza un avviso che il proprio
ordinamento **non prova** quale pacchetto Windows preferirebbe.

## Limiti

Non cambia la raccolta PnPUtil, il comando SetupAPI, il numero di nodi
(256 letti, 32 mostrati), nessuna modifica di stato Windows, nessun
driver OEM esterno importato, scaricato o installato.

Questo è un confronto dei pacchetti **già nel Driver Store**, NON la
disponibilità di aggiornamenti NVIDIA/Intel/Killer o Realtek. Il gap
#91, i test hardware Recoil 16 #136 e il ranking per INF/CAT esterni
#130 rimangono da validare su macchina reale. Firma Authenticode
del programma #283 non è coinvolta.
