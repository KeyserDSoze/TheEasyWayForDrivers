# INF OEM con virgolette non chiuse: nessun falso aggiornamento (#389)

## Difetto

Il lettore delle righe logiche INF conservava, come prova di compatibilità OEM, il primo HardwareID in una Models directive anche quando un alias successivo iniziava una stringa tra virgolette senza chiusura. Ad esempio: `%Gpu%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA,"PCI\VEN_8086&DEV_51F1`. La sintassi danneggiata non può giustificare un match esatto e tanto meno un'ipotesi di aggiornamento.

## Correzione

`InfLogicalLines.Read` controlla il bilanciamento delle virgolette **sulla direttiva logica completa**, dopo la rimozione dei commenti realmente esterni e la ricomposizione delle continuazioni fisiche. Una virgoletta aperta senza chiusura fa fallire il parsing con `InvalidDataException` prima che `InfModelEvidence` restituisca un solo HardwareID.

Le virgolette chiuse, le coppie escape `""`, i semicoloni dentro `%token;qualifier%` o stringhe tra virgolette, e i commenti reali esterni continuano a funzionare. Restano invariati i limiti di analisi (INF 4 MiB, singola riga 32 Ki caratteri, 64 continuazioni).

Le pipeline di confronto ZIP/CAB/cartella isolano il singolo INF corrotto: non lo contano come driver OEM nuovo, segnalano copertura parziale e possono proseguire sugli altri INF sani di NVIDIA/Intel/Killer. Nessun EXE eseguito, nessun download/installazione e nessun nuovo GUID WUA.

## Limiti

I test sono fixture INF sintetiche, non una misurazione dei pacchetti originali del PCSpecialist Recoil 16. Restano aperte #126 per QA reale, #91/#136 per il benchmark Driver Easy otto-vs-due, #85 per Windows E2E e #283 per Authenticode.