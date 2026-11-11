# WUA — fairness per bus dei principali vendor (#365)

## Caso riproducibile

Un singolo GUID WUA può dichiarare molte entry \`PCI\VEN_10DE&DEV_...\` NVIDIA,
poi entry audio \`HDAUDIO\FUNC_01&VEN_10DE&DEV_009B\`, software component
\`SWC\VEN_10DE&DEV_...\`, \`USB\VID_0955&PID_...\` e platform
\`ACPI\NVDA0820\`. Quando l'elenco supera 1.024 ID, il selettore ne conserva
960 in ordine e usa 64 slot vendor-fair. I gruppi NVIDIA/Intel/Realtek/AMD
e vendor rete sono separati, ma le entry *all'interno dello stesso gruppo*
prima di questa patch restavano first-seen: 64 modelli PCI tardivi potevano
oscurare tutti gli altri bus validi dello stesso produttore.

## Soluzione

Per i soli gruppi dei sette produttori principali, un bus modello
**integralmente validato** viene rappresentato prima delle ripetizioni
dello stesso bus. Il bus è letto dal prefisso PnP prima della prima
barra rovesciata, **dopo** il controllo rigoroso di \`GetVendorGroup\`.
I sette gruppi principali mantengono il round-robin originale tra
produttori; i gruppi SoundWire SDCA, SNDW, ACP e altri produttori rimangono
invariati. Gli ID del primo blocco da 960 restano immutati.

Non cambiano i **64 slot**, il tetto IPC di **1.024 ID** per GUID,
il limite di letture COM o la scelta dei pacchetti. Le stringhe
\`VEN\` senza \`DEV\`, \`VID\` senza \`PID\`, audio incompleto, attributi
inventati e percorsi di istanza non ottengono slot. La correlazione
successiva richiede sempre l'ID originale esatto Windows PnP.

## Verifica e limiti

Test sintetici: flood di oltre 200 modelli NVIDIA PCI e 180 modelli
Intel PCI prima di USB, audio, DCH SWC e ACPI; selezione combinata
fra fonti; negativi di sicurezza; mantenimento dell'ordine con
meno di 1.024 entry; nessun GUID aggiuntivo.

La patch NON prova una nuova offerta Windows Update su hardware reale
né che un driver OEM sia applicabile/installabile. Gap #91,
benchmark Recoil 16 #136 e fonti OEM #100 rimangono aperti.
