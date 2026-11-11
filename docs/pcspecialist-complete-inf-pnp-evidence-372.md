# PCSpecialist: conteggi completi INF/PnP oltre il limite di 80 righe (#372)

Il confronto locale di ZIP, CAB, PE-ZIP e INF analizza l'inventario PnP
limitato a 2.048 dispositivi, ma il report visualizza solo le 80 righe
selezionate. Prima la riconciliazione con l'elenco HTML PCSpecialist
contava soltanto quelle righe e poteva dichiarare 80 match per un
pacchetto che ne dichiarava 124, nascondendo evidenze NVIDIA/Killer.

Ora il report conserva una lista limitata di ordinali **locali transitori**
per tutti i device con Hardware ID/Compatible ID dichiarato nell'INF,
separata dai match di solo modello pubblico/SUBSYS senza REV.
ZIP, CAB vuoti, cartelle multi-lotto e ZIP annidati mantengono questa
evidenza completa, deduplicata per dispositivo: quando un archivio
presenta indizi review-only e un altro un match PnP forte per lo stesso
device, prevale il match forte senza duplicare i conteggi.

La riconciliazione usa questi totali, mantenendo il limite UI 80.
Gli ordinali non sono seriali, ID PnP, percorsi, credenziali né
numeri d'ordine, e non vengono esportati nell'interfaccia. Report
legacy privi dei nuovi ordinali restano parziali e non inventano
conteggi di corrispondenze non osservate.

Una dichiarazione INF **non** verifica firma CAT, provenienza,
versione necessaria, rank Windows o installabilità. Non si scarica,
estrae per installare o esegue alcun driver. #120/#124/#91/#136
richiedono ancora la prova con veri pacchetti e hardware Recoil 16;
la firma Authenticode #283 resta non configurata.
