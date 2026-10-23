# Schermata iniziale guidata (#88)

La Home di OmegaDrive mostra un'azione primaria: **Analizza il PC**.
Le sezioni Driver installati, Aggiornamenti disponibili, Provider OEM,
Discovery+, Cronologia, Diagnostica e Impostazioni restano disponibili
solo aprendo **Dettagli avanzati**. Tornare alla Home non elimina i risultati.

## Sequenza del pulsante

1. Diagnostica del servizio (senza autorizzare installazioni).
2. Inventario WMI/PnP dei dispositivi e dei codici problema Windows.
3. **Ricerca approfondita Windows Update**: include canali WUA configurati e
   candidati avanzati/nascosti nel confronto; gli update avanzati/nascosti
   **non vengono selezionati automaticamente**.
4. Rilevamento dei provider Intel/NVIDIA/AMD e del produttore del PC;
   stato e percorso di supporto ufficiale, non una ricerca di pacchetti
   installabili presso ogni produttore.
5. Verifica disponibilità aggiornamenti di OmegaDrive.

Errori recuperabili di un singolo step producono una Home **parziale**
con un elenco delle fasi non concluse; un provider non disponibile non deve
impedire di consultare le altre fonti. La Home mostra dispositivi,
problemi WMI e candidati WUA senza equiparare il numero di fonti OEM a driver
compatibili. I dettagli per dispositivo, i link del produttore e i candidati
si trovano nelle schede avanzate. Alcuni cataloghi OEM (Dell/Lenovo/HP) hanno
procedure esplicite, requisiti e verifiche proprie e non sono interrogati
silenziosamente dal pulsante Home.

## Sicurezza e trasparenza

- Nessun download/installazione automatica di driver, BIOS o firmware.
- Lo scanning su Windows Update non prova che esistano driver per ogni hardware,
  né che un candidato WUA sia compatibile con un dispositivo preciso.
- «Fonti OEM applicabili» è un conteggio di canali di assistenza, non di update.
- Quando un passaggio fallisce, l'app non deve mostrare «analisi completa».
- I link OEM conducono ai flussi ufficiali; nessuno scraping privato.

## QA da eseguire (#5, #85)

- [ ] Windows 11: primo avvio Home, bottone accessibile via tastiera e tastiera
      screen reader, focus, DPI 100/150/200%, viewport piccola.
- [ ] PC con 0/1/N update WUA, PC offline, policy aziendali WUA, servizio fermo;
      verificare status parziale.
- [ ] PC Dell/Lenovo/HP e sistema senza OEM riconosciuto, report e accesso ai
      percorsi ufficiali senza falsi «driver trovati».
- [ ] Verificare che nessun click su «Analizza il PC» avvii installazioni.
- [ ] Azioni Home «Vedi aggiornamenti», «Dispositivi da controllare» e «Fonti
      ufficiali» aprono esattamente la sezione dettagliata prevista.
- [ ] Conferma esplicita per qualsiasi installazione WUA separata dalla Home.

Miglioramenti di discovery multi-provider e resoconto per fonte: #89.
