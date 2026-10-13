# OEM CAB static directory (#78)

Il lettore `OemCabIndex` analizza esclusivamente l'header MSCF, i descrittori CFFOLDER e la tabella CFFILE di CAB locali esplicitamente selezionati. Segnala i file in Discovery+ come **non decompressi, non autenticati e non installabili**. Non usa expand.exe né esegue il payload.

Fail-closed: header e versione, offset e lunghezze dichiarate, CAB singolo (nessuna catena multi-volume), max 64 folder, max 2048 file, path traversal, nomi duplicati, file fino a 512 MiB e somma 2 GiB. Encoding filename CAB diverso da UTF-8 e flag riservati/multipart: nessuna conferma. La validazione directory non dimostra integrità dei blocchi dati né firma CAT.

Non copre CAB nascosti in EXE/MSI, estrazione, hashing e membership CAT. Servono QA Windows #5 e policy #46.
