# WUA IWindowsDriverUpdateEntry: USBSTOR oltre il limite IPC (#384)

## Difetto

La collezione \`IWindowsDriverUpdate4.WindowsDriverUpdateEntries\` può
contenere migliaia di associazioni PnP per **un solo GUID WUA**.
OmegaDrive legge un massimo di 2.560 entry, mantenendo **1.024**
HardwareID per IPC. Esistono 64 slot di rappresentanza equa,
che proteggono NVIDIA/Intel/Killer/Realtek, altri vendor,
SoundWire e audio moderno quando i primi 960 record non bastano.

Il matcher WUA/INF già riconosce un USBSTOR specifico:
\`USBSTOR\\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00\`.
Ma gli ID USBSTOR non hanno volutamente un modello ricercabile nel
Microsoft Update Catalog pubblico (il browser non deve esporre ID
privati di periferica). Quindi il selettore dei 1.024 ID non li
inseriva in alcun gruppo prioritario: un ID arrivato dopo il
millesimo poteva sparire dal trasporto, nonostante la lettura COM
avesse già trovato quel candidato.

## Soluzione

Il parser \`UsbStorPnpMatchPolicy\` espone ora unicamente una
**chiave di famiglia** vendor + tipo ricavata da un HardwareID
USBSTOR già rigorosamente validato, nelle forme strutturata,
padded e untyped. La chiave è solo per la priorità/round robin:
**non** entra nell'IPC, in query browser, o nel confronto driver.

\`WuaDriverEntryIdSelection\` assegna a tali ID un gruppo separato
dal pool dei vendor PCI rari. Prima vengono rappresentate famiglie
diverse, poi varianti dello stesso produttore. Restano invariati:
- primi 960 ID nell'ordine WUA;
- 64 slot protetti complessivi, 1.024 ID massimi nel trasporto;
- massimo 2.560 letture WUA, coverage incompleta segnalata;
- nessuna installazione, nuovo GUID, inferenza di versione OEM,
  o apertura di un catalogo con seriali locali.

## Verifiche

Test Core: famiglie USBSTOR strutturate e padded; class-only
\`GenDisk\`, vendor-only, REV malformate, ID di istanza, product
falsi, flood di 150 vendor PCI e flood di prodotti di un solo
produttore; merge di due fonti WUA sullo stesso GUID; confronto INF
con revisione puntata e nessuna perdita di privacy.

Test Service: COM fake reader con 1.752 entry e raccolta lunga
campionata oltre 5.000 entry: ID tardivi visibili e \`Truncated=true\`.
La copertura effettiva dipende dal COM WUA e dai driver del PC;
il benchmark Driver Easy otto-vs-due su Recoil 16 resta #91/#136.
