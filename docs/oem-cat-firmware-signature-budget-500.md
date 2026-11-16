# Fairness dei controlli firma INF/CAT sui pacchetti OEM (#500)

## Diagnosi
La pianificazione della verifica guidata delle firme esamina INF già
correlati ai dispositivi PnP del computer, ma dispone di solo **12**
controlli di firma CAT e membership INF per richiesta. Il selettore
premiava solo la copertura dei dispositivi e non distingueva i package
firmware/BIOS dagli INF driver ordinari, già separati nel confronto
ZIP/cartella in #497/#498. Una cartella con firmware duplicati poteva
rinviare il controllo di INF NVIDIA Display, Intel/Killer o Realtek.

## Comportamento
- `OemFolderCatalogAuditPlan.Create` classifica una volta per INF
  `Class=Firmware`, `Class=BIOS` e il `ClassGuid` Microsoft Firmware
  (anche nel caso di Class contraddittoria) mediante la stessa funzione
  già usata dal comparatore.
- Il nuovo selettore ha **quattro fasce**, in questo ordine:
  (1) nuovi PnP attraverso driver ordinari;
  (2) nuovi PnP attraverso firmware, solo revisione;
  (3) ripetizioni di driver;
  (4) ripetizioni di firmware.
  Within ogni fascia sceglie la riga con maggior numero di dispositivi
  PnP non ancora rappresentati; a pari merito preserva l'ordine.
- Le firme firmware non vengono soppresse: se sono la sola prova di un
  dispositivo o resta budget, possono essere esaminate come **firmware**,
  senza essere conteggiate come normali driver o aggiornamenti.
- `FirmwareInfChecks` e `FirmwareInfDeferred` distinguono i
  controlli pianificati e rinviati; il riepilogo utente non espone
  percorsi, ID istanza o dati privati.
- Restano limiti 12 verifiche, 384 INF, 96 MiB aggregati, 32 MiB CAT
  e gli altri controlli di sicurezza relativi alla cartella.

## Verifica
Test sintetici con 15 firmware NVIDIA più driver Nvidia/Killer/Realtek;
GUID firmware esplicito con classe apparentemente Display; solo firmware;
priorità per nuovi PnP anche con INF firmware e oltre 12 driver ordinari;
report privacy e compatibilità legacy.

## Limiti
Questo è **solo un piano** di verifiche Windows read-only. Il parser
non verifica automaticamente CAT, trust chain, INF/SYS/DLL membership
completa, ranking di pacchetti OEM non staged né applicabilità. Serve
ancora QA su Recoil 16 (#136/#91), firma CAT genuina (#128/#130) e
certificato di firma dell'installer (#283).
