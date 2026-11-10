# OmegaDrive — esportazione report QA senza etichette IPC private (#337)

`ScanCoverageText` è il report copiabile usato per verificare fonti
Windows Update e confrontare i risultati con strumenti terzi.
A differenza del pannello Home, esportava etichette Source e Vendor
arbitrarie, se brevi e senza slash. Un host WSUS, computer name o
numero d'ordine privato privo di caratteri speciali poteva così
essere copiato per errore su una issue.

La fonte Type=Driver ora viene resa soltanto se coincide esattamente
(con casing normalizzato) con uno dei nomi fissi di
`WindowsDriverSearchPlan`, incluso lo stato Microsoft Update non
registrato. Per Type=Software esiste una whitelist separata dei tre
pass ufficiali. Per il produttore hardware il report emette solo
le otto etichette canoniche effettivamente restituite dal
classificatore `VendorSourceDiscovery`. In ogni altro caso mostra
una dicitura neutra senza copiare il valore ricevuto su IPC.

Gli stati di ricerca (Completed, Partial, PolicyRestricted, Failed,
NetworkError, NotRegistered), i conteggi anonimizzati, i limiti e
le date UTC rimangono invariati. I valori COM grezzi, percorsi, GUID,
Hardware ID, seriali, nomi PC e titoli driver non sono esportati.
Non cambia alcuna policy di discovery/installazione né la quantità
di aggiornamenti verificati. Test Core simulano fonti e vendor brevi
e lunghi malevoli oltre a tutte le etichette ufficiali.

QA sul Recoil 16 (#91/#136) e certificati Authenticode (#283)
rimangono verifiche distinte.
