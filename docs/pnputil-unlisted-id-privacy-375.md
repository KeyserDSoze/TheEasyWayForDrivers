# Driver Store: redazione identificativi PnP non censiti (#375)

La sezione read-only \`PnPUtil /enum-devices /instanceid /drivers\`
mostra il ranking **locale** dei pacchetti già nel Driver Store, non
nuove offerte Windows Update/OEM. L'output può citare PnP ID
secondari o storici non inclusi nell'inventario iniziale; prima
si oscuravano soltanto DeviceId, HardwareIds e CompatibleIds
del dispositivo selezionato.

\`WindowsDriverStoreEvidence.FromCommandResult\` mantiene la
redazione degli ID noti e ora redige anche pattern generici
di identità PnP con bus/enumeratore (PCI, HDAUDIO, SWD, ROOT,
USB e altri), percorsi Windows con lettera di unità, UNC e URL.
Per evitare di mostrare nomi con spazi i percorsi e gli URL
oscurano il resto della singola riga, non l'intero output.
Righe autonome con rank, date, versioni e nomi INF come
\`oem14.inf\` restano leggibili.

\`WindowsSetupApiRankEvidence.SafeLabel\` rifiuta inoltre
etichette native contenenti frammenti hardware DEV_, PID_,
REV_, FUNC_ o MI_, oltre alle precedenti guardie VEN_/VID_/
SUBSYS_, path e URL.

Input massimo 48 KiB, caratteri controllo/bidi neutralizzati,
regex con timeout. Nessun comando nuovo, download, staging,
installazione o parsing del rank localizzato.

Uno screenshot può comunque contenere altri dati personali
non riconoscibili automaticamente: controllarlo prima di
condividerlo. La QA sul Recoil 16 (#132/#136) manca ancora;
nessuna nuova disponibilità di driver (#91) è dimostrata.


## Righe PnPUtil con seriali contenenti spazi (#407, 12 novembre 2026)

Per gli identificatori di istanza non censiti dal dispositivo selezionato, una sostituzione della sola sottostringa corrispondente a `PCI\\VEN_...` o `USB\\VID_...` non è sufficiente: il suffisso e i dati privati accanto all'ID possono contenere **spazi** o caratteri fuori dal pattern. Ora `WindowsDriverStoreEvidence.FromCommandResult` sostituisce **l'intera riga** che contiene sintassi PnP con una dicitura anonima fissa, **prima** delle sostituzioni di singoli ID noti, percorsi locali e URL. È una misura intenzionalmente prudente: se rank e ID compaiono sulla stessa riga, quella riga viene oscurata; le righe indipendenti con nomi INF, date, rank e versioni continuano a essere visualizzate senza interpretazione.

Test sintetici con PCI, USB, HDAUDIO, ACPI, SWD e ROOT, suffissi con spazi, dati accanto agli ID e altri dati sensibili garantiscono che nessun frammento venga mostrato nella vista locale. Non cambia il comando di sola lettura `pnputil /enum-devices /instanceid /drivers`, i limiti, o la distinzione tra rank di pacchetti **già nel Driver Store** e aggiornamenti OEM/WUA. QA Recoil 16 e pacchetti reali restano nelle issue #132/#130/#136.
