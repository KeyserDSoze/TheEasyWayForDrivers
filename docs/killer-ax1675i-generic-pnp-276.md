# Killer Wi-Fi AX1675i: rilevamento PnP con nome Microsoft generico (#276)

## Problema
La matrice degli otto componenti (#91) usa una scheda Killer Wi-Fi 6E
AX1675i (211NGW). Quando Windows non ha ancora il driver dedicato, può
mostrare il dispositivo come "Network Controller", produttore/provider
Microsoft. Il generico VEN_8086 lo portava alla sezione Intel, senza
identificare il supporto Wi-Fi/Killer.

## Regola conservativa
Un nuovo detector legge solo Hardware/Compatible ID locali e richiede
la **tripla** bus/vendor/device/subsystem specifica per AX1675i:

- PCI\VEN_8086&DEV_51F0&SUBSYS_16721A56
- PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56

È ammesso il normale suffisso PnP dopo '&', e sono rifiutati
prefissi simili, altre SUBSYS, ID istanza, spazi/controlli,
DeviceClass non 'Net'. Se PnPClass è assente, il nome deve
comunque indicare un dispositivo di rete. Una scheda Intel con
solo VEN_8086 o DEV_51F1 resta **Intel**, non Killer.

Condivisione: matrice ReferenceDriverCoverage, handoff supporto
ufficiale Intel Wi-Fi, VendorDriverEvidence per versioni installate,
DriverSourceAdvisor (Killer / Intel e OEM notebook).

## Sicurezza
Si identificano **componenti hardware** già inventariati e GUID
Type=Driver già restituiti dalle fonti WUA; nessun catalogo
esterno consultato automaticamente, nessuna nuova versione dedotta
e nessuna modifica all'installazione/QuickUpdatePolicy.
I report aggregati non includono ID, GUID, seriali o nomi locali.

Esempio di hardware documentato:
https://discussion.fedoraproject.org/t/kernel-6-11-3-200-fc40-network-wifi-ko-after-wakeup-from-standby/133879/9

Per verificare gli 8 candidati indicati da Driver Easy rimane
necessario il test sul Recoil 16 (#91, #136).
