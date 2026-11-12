# SetupAPI: recupero read-only SUBSYS OEM da VEN+DEV WMI (#388)

## Perché serve

Win32_PnPEntity può già restituire PCI VEN_10DE&DEV_28A0 (NVIDIA) o PCI VEN_8086&DEV_51F1 (Killer) senza il SUBSYS della variante notebook. Prima della fix, SetupAPI veniva interrogata solo se mancava un HardwareID specifico: non era quindi possibile correlare un INF OEM che dichiara esclusivamente lo stesso VEN+DEV con SUBSYS, anche se Windows disponeva già del valore completo tramite SetupAPI dello stesso Device Instance ID.

## Regola di arricchimento

- L'ulteriore lookup è read-only, sul DeviceInstanceID esatto già noto, limitato a modelli PCI specifici senza SUBSYS, con un unico modello pubblico VEN+DEV coerente e vendor PnP riconosciuto.
- Gli HardwareID WMI restano primi e autorevoli. Si aggiungono soltanto HardwareID ottenuti dal dispositivo SetupAPI che descrivono lo stesso VEN+DEV e un unico SUBSYS validato. Nessun dato inventato dal nome o provider del driver.
- DEV, VEN, bus, SUBSYS o revisioni discordanti, stringhe di istanza/percorso, dati nativi incompleti o oltre il tetto di 128 ID causano il rifiuto di tutte le aggiunte OEM. Un SUBSYS esclusivamente in CompatibleIDs non viene promosso.
- I CompatibleID nativi opzionali sono limitati allo stesso modello PCI oppure a una vera classe PCI. Gli identificativi WMI preesistenti non sono modificati.
- Solo 96 lookup opzionali OEM per scansione, dentro il totale immutato di 2048. Le ultime 256 letture restano riservate all'HardwareID assente/generico, compresi NVIDIA, Killer, audio.

## Verifiche e limiti

Test Core su NVIDIA/Intel Killer/Realtek, WUA GUID esatto, Home, benchmark 8 categorie, inventario vendor, handoff OEM, ZIP/INF locali, revisioni, contaminazione compatible cross-vendor e privacy; Service tests sul budget e sulla riserva di lookup.

Non aggiunge WUA GUID, richieste a siti o installazioni. Un ID con SUBSYS recuperato resta solo evidenza diagnostica read-only, non prova di versione nuova, compatibilità Windows, firma CAT o installabilità. Rimangono da misurare sulla macchina reale #91/#136, release gate #85 e firma #283.