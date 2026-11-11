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
