# CopyFiles INF: lettura conservativa di sorgenti SYS/DLL (#499)

## Problema
Nel report guidato delle firme INF/CAT e dei file OEM referenziati,
`InfDependencyPreview` gestiva le virgolette nelle tabelle
`SourceDisksNames`, `SourceDisksFiles` e `AddService`, ma non in
`CopyFiles` e nelle sezioni file-list. `String.Split(',')` perdeva nomi
validi come `"@nvlddmkm.sys"` e `"target.dll","source.dll"` o
interpretava erroneamente frammenti di righe malformate come file SYS/DLL.

La verifica read-only deve riconoscere sorgenti esistenti **solo se
l'intera sintassi della voce è interpretabile**. `Present` non è fiducia
in una firma digitale né significa pacchetto installabile.

## Modifica
- Usato un unico parser CSV-INF che considera delimitatori fuori dalle
  virgolette, le virgolette doppie escaped e rifiuta suffissi non validi.
- `CopyFiles` supporta fino a **80** campi, preservando il budget di
  80 riferimenti reali; oltre 80 è errore fail-closed, non preview
  troncata. Gli altri campi INF mantengono il loro limite indipendente
  di 8.
- La sorgente di una file-list rinominata è ricavata dal secondo campo
  solo dopo la validazione **dell'intero record e della destinazione**.
  Nomi pericolosi, quote non interpretabili, percentuali non risolte
  o path UNC non diventano percorsi locali per SignTool.
- Compatibili i token `[Strings]` sicuri con espansione singola,
  SourceDisks decorati x64/amd64 e gli avvisi quando la sorgente non
  si può stabilire.
- Test con NVIDIA, Realtek e Killer, nomi quoted, rename, escaping,
  righe malformate, 80/81 riferimenti, e `OemReferencedPayloadAudit`
  contro file SYS/DLL fisicamente presenti in una cartella di test.

## Confini
Questa è **analisi locale read-only parziale**, non interprete SetupAPI.
Non verifica da sola firma o membership CAT di nessun file, non
attribuisce ranking Windows a pacchetti non stage-ati e non autorizza
installazione. Verifica hardware reale #130/#128/#136 necessaria.
