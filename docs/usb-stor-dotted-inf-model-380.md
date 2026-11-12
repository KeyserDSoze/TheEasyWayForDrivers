# INF OEM USBSTOR con revisione puntata: confronto PnP mirato (#380)

## Difetto precedente

Il parser `InfModelEvidence` consentiva ID PnP con un singolo separatore
di bus, ma la sua regex generale non ammetteva il carattere `.`.
In un ID specifico USBSTOR, per esempio
`USBSTOR\\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00`, il punto fa
parte della versione del dispositivo e viene già validato dal parser
dedicato `UsbStorPnpMatchPolicy.IsSpecificHardwareId`. Il reader INF
scartava invece il modello presente in `[Models.NTamd64]`, rendendo
invisibile la corrispondenza al controllo mirato e al preflight OEM.

## Correzione

`InfModelEvidence.ReadCore` ora applica due sole vie:
1. L'allowlist INF generale preesistente, **senza modifiche**.
2. Una via limitata a **USBSTOR** per un modello completo che supera il
   parser USBSTOR specifico (vendor + product; eventuale revisione puntata).

La nuova via non ammette classi generiche `GenDisk`, vendor-only, percorsi
privati, doppi punti `..`, suffissi `FAKE`, bus USB/SCSI/PCI arbitrari o
identificativi PnP di istanza. Il confronto resta **esatto**; un INF che
elenca l'ID come compatibile e non come primario mantiene forza 1 anziché 2.
Il preflight verifica inoltre l'architettura e la decorazione TargetOSVersion.

Test sintetici coprono preview, matching mirato, preflight, isolamento tra
prodotti/revisioni, fallimenti e report anonimo. Nessuna offerta WUA
aggiunta; nessuna query esterna, firma CAT verificata o installazione.
I pacchetti OEM vanno ancora verificati sul Windows reale prima di
qualsiasi azione. Restano aperti #126, #100, #91, #136.
