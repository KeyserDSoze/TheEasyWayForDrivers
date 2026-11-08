# Home: verifica rapida driver locali NVIDIA, Intel/Killer, Realtek (#258)

## Problema

Dalla v0.0.110 OmegaDrive distingue nella Home **device PnP riconosciuti**
e **GUID WUA disponibili**. Gli strumenti Windows read-only sono stati
aggiunti separatamente: `WindowsSetupApiRankReader` interroga i rank
dei driver **già presenti nel sistema** per un Device Instance ID,
e `WindowsDriverStoreEvidenceReader` consulta il comando ufficiale
`pnputil /enum-devices /instanceid ... /drivers`. Questi strumenti
erano però nascosti nei dettagli: occorreva cercare a mano un device
fra centinaia di righe per capire se il Recoil 16 avesse pacchetti
locali NVIDIA, Intel/Killer o Realtek non ancora interpretati.

## Nuova esperienza

Nelle tre schede **NVIDIA**, **Intel / Killer** e **Realtek** della
Home, dopo **Analizza il PC**, compare **«Verifica driver locali»**.
Il pulsante è disabilitato se l'inventario PnP è fallito o se non
esiste un device della categoria corrispondente.

Il clic esplicito:

1. Sceglie nell'inventario già acquisito un **Device Instance ID reale**,
   senza costruire identificatori artificiali o usare un nome simile di
   un dispositivo appartenente a un altro gruppo.
2. Preferisce **GPU Display** per NVIDIA (non audio HDMI),
   **Killer Net/Wi-Fi** per Intel/Killer (non Bluetooth/GPU/chipset),
   e **Realtek MEDIA/audio** prima della scheda Ethernet.
   A parità di classe preferisce i dispositivi con problemi Windows.
   Duplica PnP per Instance ID case-insensitive senza aumentare le righe.
3. Espande la sezione dettagli **«Driver locali · rank Windows
   SetupAPI e PnPUtil»**, seleziona il dispositivo e usa **la stessa**
   routine read-only già presente in OmegaDrive.
4. Mostra il risultato **solo localmente**. I report di copertura
   copiabili non acquisiscono Device Instance ID, percorsi, seriali
   o output raw da PnPUtil.

I limiti del lettore esistente restano invariati: fino a 256 rank
SetupAPI letti, 32 mostrati e 25 secondi di budget PnPUtil.
Non vengono eseguite nuove query nel corso della scansione iniziale.

## Limiti e sicurezza

I driver **già presenti nel Windows Driver Store** non sono
automaticamente driver aggiornabili, anche quando hanno una
versione maggiore o un rank migliore; non includono gli
installer OEM esterni ZIP/CAB che non sono stati aggiunti al sistema.

Il comando PnPUtil è a parametri fissi di **sola lettura**;
non esegue `/add-driver`, `/install`, `/scan-devices`,
`/restart-device` o modifiche WUA. Nessun nuovo
`DriverUpdateInfo`, nessun aggiornamento OEM,
nessun cambio a `Aggiorna tutto`.

## QA

Test deterministici su GPU NVIDIA dal nome generico Microsoft,
NVIDIA audio con errore, Killer Wi-Fi, Intel Bluetooth e grafica,
Realtek HDAUDIO e rete, device duplicati, Intel Host Bridge escluso,
inventario non verificato, gruppi sconosciuti, stato delle schede
e garanzia `IsInstallable=false`.

La prova sul Recoil 16 e il confronto **2 offerte WUA contro
8 candidati Driver Easy** restano aperti (#91, #136). I rank
locali forniscono un secondo tipo di evidenza ma non aumentano
da soli la disponibilità nei cataloghi dei produttori.
