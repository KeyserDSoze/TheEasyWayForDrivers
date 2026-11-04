# Lenovo.Client.Update — ricerca di driver necessari (#102)

## Perché questo connettore è diverso dai semplici handoff

La documentazione ufficiale Lenovo descrive `Get-LnvUpdate` come una
verifica della **macchina corrente**: senza `-All` il comando restituisce
di norma aggiornamenti ritenuti necessari (applicabili e non installati).
Questo permette a OmegaDrive di mostrare **metadati di pacchetti driver
segnalati dal produttore**, invece di un semplice collegamento al sito.

Fonte ufficiale:
https://docs.lenovocdrt.com/guides/lcu/functions/get-lnvupdate/

## Percorso utente

1. Premere **Analizza il PC**.
2. Su un PC identificato come Lenovo, nella pagina dei risultati appare
   **Controlla i driver necessari con Lenovo**.
3. Il click richiede consenso, segnala gli accessi a Internet e gli
   eventuali requisiti di privilegi; poi invoca unicamente il cmdlet
   `Get-LnvUpdate` di `Lenovo.Client.Update` già installato.
4. Filtra e mostra fino a 80 righe di tipo driver, con
   `IsApplicable=true`, `IsInstalled=false` e identità del pacchetto.
   Versione, data e severità sono metadati Lenovo; non sono versioni
   Windows PnP direttamente confrontabili.
5. Le righe entrano solo nella sezione **Da verificare col produttore**.
   Non entrano in `DriverUpdates` né in **Aggiorna tutto**.
6. Se il modulo non è installato, mancano privilegi, scade la verifica
   oppure la rete non risponde, l'interfaccia dice **non verificato**, non
   «nessun aggiornamento».

## Confini e sicurezza

- Modulo Lenovo.Client.Update (se disponibile): deve essere installato
  separatamente dall'utente/amministratore, senza installazione automatica.
  La documentazione Lenovo indica Windows e privilegi di amministratore.
- OmegaDrive non chiama `-All`, `Save-LnvUpdate`,
  `Install-LnvUpdate`, `Expand-LnvUpdate` né
  `-SkipSignatureCheck`.
- Il modulo può consultare server ufficiali e scaricare temporaneamente
  file di valutazione; **questa non è una scansione offline**. La
  documentazione della versione 1.0.3+ dichiara che il download degli
  installer veri è demandato a Save/Install.
- Query fissa, senza PowerShell interpolato dall'utente o seriali inviati
  da OmegaDrive; limite di tempo, output e numero di righe; errori fail-closed.
- La valutazione `IsApplicable` viene dal modulo ufficiale Lenovo, **non
  da una seconda verifica indipendente di OmegaDrive**.
- Le righe ufficiali non autorizzano download/installazioni e non
  includono BIOS, firmware o applicazioni generiche.
- La vecchia consultazione `Lenovo.Client.Scripting` con MachineType
  verificato rimane disponibile nella sezione tecnica; è una fonte di
  **metadati catalogo** distinta dai pacchetti necessari al PC corrente.

## QA hardware (ancora richiesto)

- Lenovo compatibile con Lenovo.Client.Update installato e utenza autorizzata:
  verificare corrispondenza tra il risultato nativo del modulo e la UI.
- Modulo assente, accesso negato, rete scollegata e timeout.
- Nessuna voce BIOS/firmware, mai pulsanti di installazione sulle righe Lenovo.
- Riesecuzione del controllo o di Analizza il PC: nessun risultato OEM
  precedentemente rilevato deve sopravvivere come confermato.
- Windows 11 con produttore non Lenovo: il pulsante non deve comparire.

Relazioni: #14 #91 #97 #100 #102.
