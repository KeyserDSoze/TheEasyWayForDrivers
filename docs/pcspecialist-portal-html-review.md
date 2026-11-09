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
