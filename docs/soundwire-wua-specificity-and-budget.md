# OmegaDrive — SoundWire specifico e quote WUA per audio moderno (#333)

## Problema
`SOUNDWIRE\\AGGREGATOR` e `SOUNDWIRE\\AGGREGATEDSPEAKER`
sono classi di aggregazione condivise da più dispositivi. Prima il
matcher WUA/INF, basandosi sulla sola identità letterale, poteva
attribuire erroneamente una singola offerta WUA a ciascuna periferica
che esponeva quel Compatible ID.

I codec moderni, invece, possono dichiarare gli identificativi
`SOUNDWIRE\\SDCA_XX&MAN_XXXX&FUNC_XXXX` con campi strutturati
`TYPE/VER/ADR/LID/UID/SUBSYS`. Anche quando WUA li ha restituiti,
l'estrazione di oltre 1024 hardware ID per update poteva ometterli
perché mancava una quota prioritaria per SoundWire.

## Implementazione
Il matcher ora considera correlabili i soli ID SoundWire di famiglia
SDCA+MAN+FUNC **convalidati dalla grammatica rigorosa esistente**.
Le classi aggregatore e gli ID incompleti non provano il device.
Il confronto rimane per uguaglianza *integrale* dei token WUA/PnP o
INF/PnP; una ricerca manuale Catalogo per famiglia SDCA non equivale
a compatibilità, provenienza OEM o aggiornamento installabile.

La selezione WUA a budget 1024 conserva le prime 960 ID e distribuisce
64 posizioni residue a rotazione fra NVIDIA, Intel/Killer, Realtek,
AMD, Qualcomm, Broadcom, MediaTek **e SoundWire SDCA**. Nessun MAN
viene indovinato come vendor: la famiglia SDCA specifica è la sola
evidenza. Gli aggregatori e suffissi invalidi non consumano quote
prioritarie. Ogni GUID WUA rimane un solo aggiornamento, non una
offerta per ogni ID.

## Verifica e limiti
Regressioni Core coprono ID SoundWire completi e generici, compatibile
vs hardware, differenze SUBSYS, INF, audit privacy-safe e long tail
oltre 1024 con tutti gli altri major. Nessun package OEM viene
eseguito o marcato installabile. QA fisico Recoil 16, comparazione
Driver Easy 8-vs-2 (#91/#136), firma CAT (#128) e Authenticode
(#283) restano aperti.
