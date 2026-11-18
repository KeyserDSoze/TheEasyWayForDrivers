# Diagnostica driver installati: consenso PnP per i duplicati (#542)

## Problema
Le righe Windows WMI/PnPSignedDriver possono ripetere lo stesso
`DeviceId` con Hardware ID diversi. La vista
`VendorDriverEvidenceFinder` applicava `DistinctBy(DeviceId)` dopo
la selezione dei dispositivi rilevanti. In caso di dati contraddittori
veniva mostrata l'identità della **prima** riga, mentre Home, OEM
handoff, audit WUA e benchmark già usano il consenso
`PnpDistinctTrustedDevices` per escludere l'intera istanza.

## Intervento
La vista delle versioni installate usa ora
`PnpDistinctTrustedDevices.Create` **prima** dei filtri vendor,
del round-robin, dei conteggi e delle correlazioni WUA per ID.
Righe NVIDIA/Realtek/Intel in conflitto, o due differenti modelli
primari anche dello stesso vendor, non producono un risultato
arbitrario. Un modello valido in una seconda riga può recuperare
una prima riga generica solo quando il consenso è già verificato
sulla **stessa** istanza. Gli Hardware ID non vengono uniti.

## Ambito
Nessuna nuova offerta WUA, aggiornamento OEM, classifica o scelta
di installazione viene introdotta. La verifica su Recoil 16 reale
confrontando gli otto elementi Driver Easy (#91/#136) resta aperta;
questi sono test sintetici di coerenza della diagnostica.
