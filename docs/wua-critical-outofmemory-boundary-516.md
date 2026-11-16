# DriverSearchPassAudit: OutOfMemoryException non è un errore di catalogo (#516)

## Problema
Il runner WUA `DriverSearchPassAudit.Run` catturava ogni `Exception`
non espressamente gestita. Anche `OutOfMemoryException`, per esempio
durante una enumerazione COM dei driver, veniva trasformata in
`DriverSearchPassStatus(Outcome="Failed")`: l'applicazione poteva
proseguire con altri pass WUA, query Type=Software e cache in una
situazione di esaurimento delle risorse.

Gli accessor opzionali COM già propagano `OutOfMemoryException`; la
protezione va rispettata anche dal runner comune. L'eccezione non
equivale a «zero aggiornamenti» e non è un problema di policy WSUS.

## Modifica
- `catch (OutOfMemoryException) { throw; }` prima del catch generale.
- Nessuna invocazione del callback di normali errori di ricerca.
- La cancellazione esplicita resta propagata. Gli HRESULT noti di
  policy/rete e gli errori IO continuano ad avere status non verificati
  privacy-safe, senza messaggi COM, URL o identificativi hardware.
- I risultati non installabili delle fonti Partial, la deduplicazione
  e la policy per Aggiorna tutto non cambiano.
- La correzione non pretende di ripristinare memoria: forza la
  propagazione dell'errore critico al chiamante invece di continuare
  silenziosamente con uno stato WUA apparentemente ordinario.

## Test
`DriverSearchPassAuditTests`: OOM simulata nella query WUA (con e
senza cancellazione durante la query), zero callback ordinari, controllo
di cancellazione richiesta, HRESULT proxy/WSUS, IO e cancellazione COM
non richiesta. Nessuna necessità di allocare davvero memoria.

Rimangono aperti test Windows reali offline/WSUS (#92), release
end-to-end (#85), firma Authenticode (#283) e QA Recoil 16 (#136).
