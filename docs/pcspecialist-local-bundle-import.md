# PCSpecialist Recoil — importazione locale di una cartella driver (#262)

## Problema affrontato

OmegaDrive può confrontare **singoli ZIP/CAB** e **cartelle INF già
estratte**, ma fino alla v0.0.125 queste erano verifiche separate.
Dopo aver recuperato dal portale ufficiale PCSpecialist i driver del
proprio ordine, una directory mista può contenere decine di file
e sottocartelle; selezionarli manualmente nasconde facilmente
evidenze NVIDIA RTX 4060, Killer Wi-Fi e Realtek Audio.

Questa modifica aggiunge un'azione specifica per PCSpecialist:
**«Importa cartella driver PCSpecialist»** in
*Dettagli delle fonti → Strumenti e verifiche del produttore*,
accanto al pulsante del sito ufficiale.

**Il numero d'ordine si inserisce esclusivamente sul sito
PCSpecialist**, non in OmegaDrive. Il programma non autentica,
interroga né scarica automaticamente dal portale.

## Come funziona

Dopo aver eseguito **Analizza il PC**, sul PC con produttore
PCSpecialist riconosciuto e inventario PnP valido, l'utente:

1. Apre il portale ufficiale `https://www.pcspecialist.co.uk/download-drivers/`
   e scarica i pacchetti destinati al proprio ordine.
2. Salva gli archivi e gli eventuali INF già estratti in una
   cartella locale; clicca «Importa cartella driver PCSpecialist»,
   sceglie la directory e conferma l'analisi **in sola lettura**.
3. OmegaDrive esamina tutte le sottocartelle (al massimo 512
   directory, profondità 16, 3.000 file visitati), senza seguire
   symlink, junction o reparse point.
4. Prepara fino a **12 sorgenti** in totale: ogni ZIP/CAB vale una
   sorgente; se sono presenti INF già estratti, l'intera raccolta
   di INF vale una sorgente aggiuntiva.
5. Applica i controlli dei reader esistenti: **4 GiB**
   di archivi cumulativi; massimo **384 INF sciolti**,
   quattro lotti da 96 INF/24 MiB; limiti ZIP, CAB e
   archivi annidati come nel confronto normale.
6. Confronta tutte le sorgenti con **lo stesso inventario
   Windows PnP** e unisce i risultati per **indice anonimo di
   dispositivo**. Un NVIDIA presente in tre pacchetti rimane
   *un dispositivo distinto*, non tre aggiornamenti.
7. Visualizza conteggi, eventuali indizi di versioni superiori,
   file scartati e la lista limitata a **80 righe**.
   Quando esistono INF estratti con associazioni, consente
   come azione separata la verifica guidata delle firme
   CAT/INF e dei file referenziati.

Tutti gli archivi ZIP/CAB riconosciuti vengono verificati
prima di presentare il risultato. Se una sorgente
è illeggibile o manca, il confronto fallisce **per intero**:
non si mostra un sottoinsieme come se fosse esaustivo.

## Formati non analizzati

Gli **EXE, MSI, MSIX, 7z, RAR e ISO proprietari** non sono eseguiti
né estratti automaticamente da questo importatore; vengono
**contati come sorgenti non analizzate** e rendono il report
esplicitamente **PARZIALE**. Il percorso manuale già esistente
«Confronta ZIP/CAB/EXE multipli» rimane disponibile per i
soli EXE con vero overlay ZIP/PE supportato, sempre senza
eseguirli. Per gli installer proprietari occorre la verifica
manuale tramite il produttore, non un'installazione automatica.

Un pacchetto in una cartella denominata «PCSpecialist» **non
prova la provenienza ufficiale**, la firma CAT/WHQL,
il ranking Windows o la compatibilità del modello del PC.
Un INF con Hardware ID esatto e una versione più alta
rimane **evidenza preliminare**, NON un nuovo driver
confermato o installabile.

## Privacy e sicurezza

- I percorsi dei file e delle cartelle sono usati **solo
  in memoria locale** per l'analisi; i nomi degli archivi
  e i numeri d'ordine non entrano nei report o log.
- Nessun Device Instance ID, seriale o numero d'ordine
  viene trasmesso a PCSpecialist o altri servizi.
- Nessun eseguibile è lanciato; non si usa
  `pnputil /add-driver`, non si copia nulla nel Driver
  Store e non si cambiano `Windows Update` o
  `Aggiorna tutto`.
- Il timeout dell'importazione è **5 minuti**;
  cancellazioni, limiti e file mutati interrompono
  la verifica senza risultati parziali ingannevoli.

## QA e attività successive

Test automatici: cartelle annidate con ZIP/CAB/INF,
EXE/MSI/7z non eseguiti e censiti, deduplicazione
NVIDIA/Killer/Realtek da ZIP e INF, 12 sorgenti massimo,
13ª sorgente bloccata, no files, cancellation,
symlink se permessi Windows lo consentono, assenza
di percorsi/numero ordine nei report.

**Non chiude l'integrazione completa PCSpecialist
#120**: manca ancora un catalogo ufficiale
strutturato e verificabile per ordine, oltre a una
prova sul Recoil 16. Il benchmark Driver Easy
**2 vs 8** resta aperto (#91, #136).
