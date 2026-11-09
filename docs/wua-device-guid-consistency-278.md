# Corrispondenze WUA per dispositivo coerenti fra fonti (#278)

## Perché
Il servizio WUA già aggregava per GUID le offerte Windows Update,
Windows configurato e Microsoft Update completate. La schermata
per dispositivo, però, confrontava direttamente singole
rappresentazioni dell'offerta, applicando DistinctBy solo DOPO
il matching. Quando una fonte avanzata dichiarava l'ID NVIDIA/Killer
e la fonte ordinaria lo stesso GUID per un dispositivo Intel, il
pannello poteva mostrare come unicamente avanzata un'offerta il cui
GUID era già stato confermato nella ricerca ordinaria.

## Modifica
\`DeviceWuaMatchGroups.Prepare\` normalizza una sola volta per intera
ricerca i GUID validi, riusando il merge già in produzione del servizio.
Le identità PnP confermate provenienti da pass diversi vengono unite
entro il limite di **1024 ID aggiuntivi per GUID**; nessuna nuova
offerta nasce da un ID aggiuntivo. Metadata ordinari verificati
hanno precedenza su quelli avanzati dello stesso GUID.
\`Prepared.ForDevice\` classifica i risultati per ogni dispositivo e
applica senza modifiche \`QuickUpdatePolicy\` per separare le offerte
ordinarie dagli indizi review-only. La UI esegue Prepare una sola
volta, invece di rifare la normalizzazione per ogni riga.

La funzione originale \`Create\` rimane disponibile per vecchi
chiamanti, ma applica anch'essa questa normalizzazione, rendendo
le associazioni coerenti anche sui dati legacy.

## Sicurezza, test e compatibilità
Un GUID soltanto avanzato, nascosto, di origine sconosciuta o con
firmware continua a non diventare installabile: il servizio effettua
un nuovo controllo WUA in fase di installazione. I titoli
Type=Software e le ricerche parziali restano fuori da questi
risultati; il matching non inferisce ID da titoli/vendor/provider.
Nessun catalogo OEM aggiuntivo, download o modifica di policy.

Test: stesso GUID WUA e ID Intel, NVIDIA, Killer su fonti diverse;
Realtek oltre i 1024 ID della prima fonte; soli indizi avanzati
e sorgenti non attendibili, ID near-miss, GUID invalidi. I test
esistenti verificano ora l'identità del GUID anziché l'identità
dell'oggetto C# (la normalizzazione può creare un nuovo record).

Questa modifica risolve la coerenza per dispositivo delle offerte
**già restituite** da WUA; la prova Recoil 16 2 contro 8 richiede
ancora misure reali (#91, #136).
