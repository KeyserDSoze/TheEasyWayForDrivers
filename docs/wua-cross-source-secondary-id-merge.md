# Discovery WUA — associazioni PnP conservate anche nel merge fra fonti (#260)

## Problema individuato dopo v0.0.124

Un medesimo GUID `Type=Driver` può essere restituito da più ricerche
complete: Windows configurato, Windows Update online e Microsoft Update
(già registrato). La stessa offerta può riportare insiemi di
`IWindowsDriverUpdate4.WindowsDriverUpdateEntries` differenti.

Dalla v0.0.123 la lettura di **ogni singola fonte** considera fino a
2.048 ID e ne sceglie 1.024 in modo rappresentativo, preservando
alcuni ID NVIDIA, Intel/Killer, Realtek e altri principali vendor
che compaiono in fondo alla collezione.

Ma `WindowsDriverCandidateMerge.MergeDeviceIds` prendeva fino a
1.024 ID secondari della fonte preferita prima di guardare quelli
dell'altra fonte. Se la prima fonte riempiva tutti gli slot,
gli ID secondari dell'altra fonte sparivano anche se
perfettamente validi. L'ID primario dell'altra fonte era
già protetto, **non gli ID secondari**.

## Correzione

Il merge di due pass con lo **stesso GUID WUA** ora:

1. Mantiene la preferenza del candidato ordinario/consigliato
   rispetto al solo avanzato, come prima.
2. Deduplica gli ID `HardwareId` case-insensitive. L'ID primario
   dell'altra fonte viene preservato davanti alle liste secondarie.
3. Valida e scorre fino a 2.048 ID secondari **per pass**;
   tiene in memoria temporaneamente l'unione bounded,
   senza troncarla anticipatamente a 1.024.
4. Applica **nuovamente** il selettore vendor-fair all'unione:
   fino a 1.024 ID nel risultato IPC, mantenendo i primi 960
   dell'ordine di unione e riservando fino a 64 slot con
   round-robin fra identificativi WUA **già dichiarati** per
   NVIDIA, Intel/Killer, Realtek, AMD, Qualcomm/Atheros,
   Broadcom e MediaTek.
5. Se i vendor principali sono meno di 64, riempie i posti
   residui secondo l'ordine originale, senza inventare ID.
6. Marca `AdditionalHardwareIdsIncomplete=true` se una
   fonte era incompleta, troppo grande o alcuni ID non
   possono essere trasmessi.

Il selettore della **singola fonte** mantiene il limite
di 2.048 ID; il nuovo metodo del **merge fra due fonti**
accetta fino a **4.097 ID** (2 × 2.048 + 1 primario
dell'altra fonte) soltanto in memoria. L'IPC resta a
**1.024 ID per GUID**, senza aumento delle dimensioni
trasmesse. I GUID restano deduplicati.

## Confini di sicurezza e limiti

La correlazione per Hardware ID è **informativa**, non
certifica applicabilità OEM, ranking SetupAPI, firma CAT,
versione più nuova o installazione sicura. Non vengono
aggiunte fonti, API non ufficiali, driver download,
installazioni OEM o nuove policy Windows Update.

`QuickUpdatePolicy`, il recheck prima dell'installazione,
i risultati `Type=Software` e gli indizi di ricerche
parziali rimangono invariati. Un GUID avanzato non
diventa installabile perché è associato a un dispositivo;
la preferenza dell'offerta ordinaria è preservata
soltanto quando **quella fonte** l'ha realmente confermata.

Il report «Copia copertura» continua a riportare
**solo contatori anonimi**, senza GUID, Hardware ID,
Device Instance ID, titoli update o percorsi.

## QA

Fixture sintetiche con due ricerche aventi il medesimo
GUID, 1.024 ID primari e secondari e hardware NVIDIA RTX,
Killer Wi-Fi, Realtek HDAUDIO presente solo nell'altra
fonte; copertura delle sette famiglie, campo primario
dell'altra fonte, pass avanzati e ordinari, casi sotto
limite, doppie liste da 2.048 e report anonimi.

La verifica su PCSpecialist Recoil 16 e il benchmark
**2 candidati OmegaDrive contro 8 Driver Easy** restano
aperti (#91 e #136). Il fix può recuperare associazioni
di un GUID **già presente**, ma non crea nuovi driver
nel catalogo Windows Update.
