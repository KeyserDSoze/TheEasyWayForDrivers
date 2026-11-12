# OEM PCI SUBSYS di un duplicato PnP, senza falsi match (#387)

## Problema

Un notebook Windows può esporre due righe dello stesso DeviceId:
`PCI\VEN_10DE&DEV_28A0` e `PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA`.
La deduplicazione della v0.0.246 dava sempre priorità alla prima
riga perché già un modello PCI *pubblico* completo, perdendo il
**modello OEM specifico** che un INF locale di PCSpecialist può
dichiarare come unica corrispondenza hardware. Anche un GUID WUA
con quel preciso SUBSYS risultava non associato nell'audit read-only.

## Regola di recupero

Solo per una prima riga con **un singolo Hardware ID PCI bare VEN+DEV**,
e solo quando il vendor è PnP riconosciuto, il selettore confronta tutti
gli Hardware/Compatible ID di **ogni** riga dello stesso DeviceId:

- Gli ID specifici devono normalizzarsi allo stesso identico modello
  pubblico `PCI\VEN_XXXX&DEV_YYYY`, senza differenze di bus, vendor, DEV.
- Sono ammessi soltanto gli indizi generici PCI di classe o vendor
  coerenti. ID malformati, estranei, percorsi e stringhe private
  impediscono la promozione.
- Deve esistere un **unico** Hardware ID completo con SUBSYS in una riga
  successiva. Due SUBSYS/revisioni diverse bloccano il recupero.
  Un SUBSYS presente **solo** tra i CompatibleID non basta.
- Si usa **una riga originale completa** (senza concatenare i suoi ID
  con quelli di altre righe), conservando l'ordinale originario del
  dispositivo nel confronto OEM multi-archivio.
- Se la prima riga è già qualificata con SUBSYS o presenta più modelli,
  non si interviene. Il recupero generic-first #386 resta indipendente.

La modifica riguarda solo associazioni/diagnostica. Il confronto
versioni INF non è prova di ranking Windows, firma CAT, driver più
recente o installabilità. Nessun nuovo provider, catalogo, GUID o
installer viene creato.

## Regressioni

Test con NVIDIA GPU 4060 Laptop, Intel Killer, Realtek PCI; WUA fonte
verificata, Home, benchmark otto categorie, inventario vendor, support
handoff, ZIP/INF locale, ordinali originali, varianti senza SUBSYS e
fallimenti per DEV/SUBSYS discordanti, altri bus/vendor, dati invalidi
e CompatibleID-only. La validazione sul Recoil 16 e il confronto
otto-vs-due Driver Easy rimangono #91/#136; firma Authenticode #283.
