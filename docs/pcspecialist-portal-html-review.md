# PCSpecialist — elenco pacchetti da HTML locale (#316)

## Flusso

Il portale ufficiale https://www.pcspecialist.co.uk/download-drivers/
richiede il numero del proprio ordine e distingue i pacchetti della
configurazione esatta; Recoil 16 non basta a identificare il notebook.

1. Dopo la scansione aprire **Driver PCSpecialist** nel browser.
2. Inserire il numero ordine soltanto nel portale PCSpecialist.
3. Salvare come HTML la pagina *Download Drivers / Manuals* con i file.
4. In OmegaDrive scegliere **Leggi elenco PCSpecialist salvato**.
5. Esaminare i nomi dei pacchetti e le categorie NVIDIA/Intel/Killer/
   Realtek. Per vera compatibilità, scaricare manualmente dal portale
   i pacchetti relativi al proprio ordine e usare **Importa cartella
   driver OEM** per un confronto INF, CAT e ranking separato.

## Sicurezza e copertura

- L'HTML locale (max 1 MiB) è letto in UTF-8, senza eseguire codice,
  aprire URL, fare download, inviare il numero ordine o interrogare il
  sito in modo automatico. Solo nomi di file da link HTTPS PCSpecialist
  o dallo storage Cloudflare ufficiale, con filtri stretti.
- I parametri URL firmati, i numeri d'ordine e i percorsi non sono
  conservati in output, clipboard o log. Sono esclusi i nomi che
  sembrano contenere token, seriali, ordini o path.
- 2048 link massimi; 96 file visualizzati; l'eventuale troncamento è
  dichiarato. Input troppo grande o non riconosciuto fallisce chiuso.
- La pagina salvata NON è autenticata: il filename NON prova
  provenienza ufficiale, versione, necessità, firma o applicabilità
  Windows per il PC. Le categorie derivano solo dal testo.
- I pacchetti non sono WUA Type=Driver: IsInstallable,
  IsHardwareMatched e IsVerifiedOfficial rimangono sempre false.
  Nessuna voce entra in Aggiorna tutto.

## QA e limiti

Test xUnit sintetici: pagina Recoil, NVIDIA VGA, Intel Wi-Fi/Bluetooth,
Realtek Audio/LAN, software, firmware, link S3 firmati, redirect non
consentiti, privacy, duplicati, input malformato, budget e cancellazione.
Il confronto reale sul PC Recoil16 e gli 8 aggiornamenti Driver Easy
rimane da misurare (#91/#136), come la verifica INF dei pacchetti
scaricati (#120) e la firma Authenticode dell'app (#283).

## v0.0.177 — riconciliazione con i pacchetti scaricati (#317)

L'importazione dell'HTML da file locale è ora consentita per **ogni**
inventario Windows completo, non soltanto con SMBIOS PCSpecialist:
Recoil16 può apparire come Clevo, TongFang o "Notebook". Il collegamento
**Driver PCSpecialist** al portale, invece, continua a essere mostrato
solo con produttore PCSpecialist riconosciuto; nessun numero ordine è
inviato automaticamente.

Dopo aver importato l'elenco HTML, usare **Importa cartella driver OEM**:
OmegaDrive compara i **nomi esatti dei file** salvati nel browser con i
file ZIP/CAB/PE-ZIP e gli installer non supportati della cartella.
Non usa somiglianza/fuzzy matching, provider, modello OEM presunto o
nomi di driver per attribuire un archivio: due file omonimi in
sottocartelle diverse sono **ambigui**.

Per ciascuna voce dell'elenco ora può comparire:
- non osservata nella cartella selezionata (non prova mancanza online);
- presente ma **non analizzata** (MSI, 7z, EXE proprietario);
- archivio letto senza match PnP **visibili** (non prova assenza driver);
- solo indizio INF pubblico/SUBSYS per **revisione manuale**;
- INF con ID hardware/compatibile PnP **dichiarato**.

La presenza del file o di un ID INF non prova firma CAT, ranking,
compatibilità Windows, provenienza ufficiale o aggiornamento necessario.
I risultati della riconciliazione sono **solo una lettura locale** e
restano completamente separati da WUA Type=Driver e **Aggiorna tutto**.
La verifica a livello archivio è limitata ai pacchetti che il lettore
OEM supporta, agli INF effettivamente esaminati e ai budget di
sicurezza; gli elementi scartati e le analisi parziali restano
esplicitamente non verificati. I nomi non supportati e i percorsi sono
usati solamente **in memoria**, e nell'interfaccia sono mostrati
esclusivamente i nomi già sanificati dall'HTML. Nessun ID dispositivo,
numero ordine o token è restituito.

## v0.0.178 — cartelle contenenti soltanto installer proprietari (#318)

Se un elenco HTML PCSpecialist è già stato importato, la successiva
**Importa cartella driver OEM** può riconciliare anche una cartella con
soli MSI/7z/EXE proprietari. OmegaDrive non apre né esegue tali file:
segnala i nomi presenti come *formato non supportato*, quelli assenti come
*non osservati* e i duplicati come *ambigui*. Il riepilogo indica
**zero** archivi/INF esaminati e **COPERTURA PARZIALE**, senza dedurre
nessuna compatibilità o versione installabile. Senza elenco HTML resta
il rifiuto della cartella non analizzabile; le cartelle vuote sono
sempre rifiutate. I percorsi e i numeri ordine locali non entrano nel
report. Nessuna modifica a WUA o **Aggiorna tutto**.

## v0.0.179 — preview HTML bilanciata per categorie (#319)

Il portale può elencare molti file prima dei driver grafici/rete/audio. Il
vecchio taglio sui **primi 96** link nascondeva NVIDIA, Killer e Realtek
arrivati in fondo. Ora ogni nome valido dell'HTML (max 2048 link/1 MiB)
viene classificato **solo a fini di presentazione**; la preview seleziona
96 pacchetti con rotazione tra categorie, in ordine deterministico
indipendente dalla posizione HTML. `TotalDistinct`, `Omitted` e la
copertura **PARZIALE** restano espliciti: i nomi omessi non vengono
considerati assenti dal portale. Non si identificano driver applicabili,
non si scaricano file, non si espongono URL firmati o numeri ordine e
**Aggiorna tutto** continua a usare soltanto Windows Update.

## v0.0.180 — priorità globale PE-ZIP e HTML salvato (#320)

Il limite di **24 sonde EXE** rimane per motivi di sicurezza, ma la
scelta non dipende più dall'ordine DFS delle sottocartelle: prima si
inventariano, con limiti invariati, i file EXE eleggibili, poi si
ordinano **globalmente** per basename. Se è stato importato un HTML
PCSpecialist, i file con **basename identico** alla lista salvata
vengono verificati per primi (solo ispezione PE-ZIP read-only). Tutti
gli EXE oltre il limite o con formato proprietario sono dichiarati
**non analizzati / copertura parziale**. Nessuna attribuzione fuzzy,
nessun download, esecuzione EXE o installazione; nessun cambiamento a
WUA/Aggiorna tutto. Anche un match INF rimane un indizio, non prova
compatibilità, firma CAT o aggiornamento necessario.
