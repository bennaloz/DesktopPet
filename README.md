# Zaira Desktop Pet

Un gatto 3D che vive sul desktop di Windows: cammina sulla barra delle applicazioni e sui bordi
delle finestre, ci salta sopra, fa gli zoomies, dorme sul trespolo, mangia dalla ciotola, insegue
i bocconcini, si prende col mouse e si accarezza.

Per ora il gatto è un **segnaposto**: la volpe CC0 di Quaternius, ricolorata di grigio. Il modello
di Zaira si inserisce dopo senza toccare il codice (vedi sotto).

## Avvio

Doppio clic su `run.bat`. Per chiudere: icona nella system tray → **Esci**.

## Cosa provare

| Azione | Come |
|---|---|
| Prenderla in braccio | premi sul gatto e trascina; muovendo il mouse (o la rotella) la fai girare; rilascia per lasciarla cadere, anche lanciandola |
| Accarezzarla | passa il mouse avanti e indietro sopra il gatto senza premere: si ferma e fa le fusa (♥) |
| Darle da mangiare | doppio clic sulla ciotola rossa per riempirla; quando ha fame ci va da sola. Se la ciotola è vuota si siede lì e reclama (!) |
| Bocconcino | tray → **Lancia un bocconcino**: cade dall'alto, lei corre a prenderlo |
| Spostare ciotola e trespolo | trascinali; ricadono sulla superficie sotto (anche sopra una finestra) |
| Salti sulle finestre | nessuna azione: quando esplora salta sui bordi superiori delle finestre visibili (non su quelle massimizzate). Se sposti la finestra lei la segue, se la chiudi o la minimizzi cade |
| Arrampicata | se il bordo è troppo alto per un salto (oltre ~460 px) si aggrappa al lato della finestra, sale e ci monta sopra. Anche mentre sale, se sposti la finestra la segue e se la chiudi cade |
| Zoomies | quando la voglia di giocare è al massimo corre avanti e indietro e salta dove capita |
| Dormire | quando è stanca va sul trespolo e dorme (z) |
| Stato | passa il mouse sull'icona della tray: fame, energia, voglia di giocare, contentezza, ciotola |
| Se sparisce | tray → **Chiama Zaira** |
| Pausa | tray → **Pausa** |

I bisogni cambiano in tempo reale (fame piena in circa 2 ore, stanca dopo circa 2 ore sveglia) e vengono
salvati in `%APPDATA%\Godot\app_userdata\Zaira Desktop Pet\save.json`. Il log è nello stesso posto (`zaira.log`).

## Mettere Zaira al posto della volpe

1. Crea `cats/zaira/` con il GLB riggato (es. `zaira.glb`) e un `profile.json` copiato da `cats/fox/profile.json`.
2. In `profile.json` imposta `model`, `length_px` (lunghezza a schermo) e, per ogni azione, i nomi
   delle animazioni presenti nel GLB (`idle, walk, run, jump, fall, land, sit, sleep, eat, meow, purr, held`).
   Le azioni mancanti ricadono su una simile (es. `sleep` → `sit`).
3. Se il modello guarda di lato o all'indietro, correggi `yaw_offset_deg` (90 / 180 / -90).
4. `material_colors` serve solo alla volpe: per Zaira, con la texture di Tripo, va tolto.

La cartella `cats/zaira` ha la precedenza sulla volpe; per forzarne una: `run.bat -- --cat=fox`.

## Autotest

```
run.bat -- --selftest            giro dei comportamenti con screenshot
run.bat -- --selftest-windows    serve tools/test-window.ps1 in parallelo: sale su una finestra, la segue, cade
                                 (finestra alta, da scalare: tools/test-window.ps1 -Top 200 -Height 520 -MoveAt 22 -CloseAt 40)
run.bat -- --selftest-input      presa, lancio, carezze, doppio clic (eventi iniettati in Godot)
run.bat -- --selftest-mouse      mouse vero: in parallelo tools/test-mouse.ps1 (sessione sbloccata!)
dotnet test tests/Core.Tests     test della logica (superfici, fisica, percorsi, comportamenti)
```

Screenshot e log finiscono in `%APPDATA%\Godot\app_userdata\Zaira Desktop Pet\`.

## Struttura

- `src/Core` — logica pura senza Godot, testata: mappa delle superfici, fisica, percorsi, bisogni, comportamenti, salvataggio.
- `src/Platform` — Win32: finestre visibili, monitor, stile della finestra overlay.
- `src/Game` — Godot: overlay trasparente, gatto 3D, props, mouse, tray, autotest.
- `docs/superpowers` — spec e piano.
