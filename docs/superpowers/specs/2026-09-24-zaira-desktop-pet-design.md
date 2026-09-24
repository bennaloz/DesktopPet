# Zaira Desktop Pet — design

Data: 2026-09-24. Stato: approvato a voce in chat (sezioni 1–2); sezioni 3–4 decise in autonomia
con i default proposti, da rivedere insieme.

## Obiettivo

Un gatto 3D (Zaira) che vive sul desktop di Windows: cammina sulla barra delle applicazioni e sui
bordi superiori delle finestre, ci salta sopra e ne cade, fa gli zoomies, dorme su un trespolo,
mangia da una ciotola, insegue bocconcini, si prende e si fa girare col mouse, si accarezza.

Prima si costruisce l'app con un modello segnaposto (volpe Quaternius, CC0, tinta grigia);
il modello di Zaira (Tripo, riggato) si sostituisce dopo senza toccare il codice.

## Scelte

| Tema | Scelta |
|---|---|
| Motore | Godot 4.7 .NET (C#), renderer Compatibility |
| Finestra | una sola overlay trasparente, senza bordi, sempre in primo piano, grande quanto il desktop virtuale (−1 px in altezza per evitare il fullscreen esclusivo), esclusa da taskbar/Alt-Tab (WS_EX_TOOLWINDOW), non prende il focus |
| Coordinate | pixel fisici dello schermo (y verso il basso) nella logica; camera ortografica 1 unità = 1 px nel rendering |
| Clic | `mouse_passthrough`: la zona cliccabile è il rettangolo dell'oggetto sotto il cursore (gatto, ciotola, trespolo, bocconcino); durante un trascinamento l'intera finestra |
| Superfici | bordi superiori scoperti delle finestre visibili + bordo superiore dell'area di lavoro di ogni monitor (pavimento = sopra la taskbar) + ripiano del trespolo; lati scoperti delle finestre come pareti per l'arrampicata |
| Finestre | Win32 via P/Invoke, 8 Hz: EnumWindows (ordine z), IsWindowVisible, IsIconic, DWMWA_CLOAKED, DWMWA_EXTENDED_FRAME_BOUNDS, esclusione tool window e della nostra |
| Bisogni | fame, energia, voglia di giocare (0..1), evolvono nel tempo, salvati su disco |
| Cibo | ciotola trascinabile sul pavimento (doppio clic = riempi) + bocconcini lanciati dal menu tray |
| Tray | StatusIndicator: chiama il gatto, lancia bocconcino, riempi ciotola, pausa, esci |

## Architettura

```
src/
  Core/            logica pura, senza Godot (testata con xUnit)
    Geometry.cs    RectI, Segment, Vec2
    SurfaceMap.cs  finestre ordinate per z → piattaforme/pareti scoperte
    Navigator.cs   percorsi fra piattaforme (camminata, salto, discesa)
    Needs.cs       bisogni e loro evoluzione
    CatBody.cs     fisica 2D del gatto (appoggio, caduta, salto balistico, trascinamento)
    CatBrain.cs    macchina a stati dei comportamenti
    SaveData.cs    stato persistente (JSON)
  Platform/        Win32 (P/Invoke), WindowTracker
  Game/            nodi Godot: Main, Overlay, CatNode, visual, props, tray, input
cats/<nome>/       GLB + profile.json (mappatura azioni → animazioni)
tests/             xUnit su src/Core
```

### Contratto del modello (`cats/<nome>/profile.json`)

Il codice usa azioni logiche: `idle, walk, run, jump, fall, land, sit, sleep, eat, meow, purr, held`.
Il profilo indica per ogni azione l'animazione del GLB (o più varianti), se va in loop, se va
fermata sull'ultimo fotogramma; più lunghezza a schermo in pixel, tinta opzionale, orientamento.
Azioni mancanti ricadono su un'alternativa (es. `sleep` → `sit` rallentato).

### Comportamenti (CatBrain)

Stati: Idle, Wander, Travel (verso un obiettivo via Navigator), Climb (sul lato di una finestra quando il
bordo è oltre la portata di un salto), Zoomies, Eat, Sleep, Sit/Groom,
Meow (ha fame e la ciotola è vuota), ChaseTreat, Petted, Held, Falling, Landing.
Scelta per utilità: fame alta → ciotola; energia bassa → trespolo; voglia di giocare alta → zoomies;
altrimenti vagare/sedersi/saltare su una finestra vicina.

### Interazioni

- Premi sul gatto e trascina: lo tieni in mano (posa "held"), il movimento orizzontale del mouse lo
  fa ruotare, la rotella anche; rilasciandolo cade con la velocità del lancio.
- Passa il mouse sopra il gatto muovendolo: carezza; dopo un po' si siede e fa le fusa, sale la contentezza.
- Ciotola: trascinabile, doppio clic riempie. Bocconcino: dal menu tray, cade dall'alto vicino al gatto.

### Errori e robustezza

- Finestra di appoggio chiusa/minimizzata/spostata: se sparisce il gatto cade; se si sposta la segue.
- Gatto fuori dallo schermo o bloccato: "Chiama il gatto" dalla tray lo riporta sul pavimento del monitor principale.
- Scansione delle finestre fallita: la mappa resta quella precedente; errori a log (`zaira.log` nella cartella utente di Godot).

## Test

- xUnit su SurfaceMap (occlusione, pavimento, finestre minimizzate), Navigator, Needs, CatBody.
- Autotest in gioco: `--selftest` (giro dei comportamenti), `--selftest-windows` (finestra vera:
  salto/arrampicata, inseguimento, caduta), `--selftest-input` (eventi mouse iniettati),
  `--selftest-mouse` (mouse reale con tools/test-mouse.ps1); log e screenshot nella cartella utente.

## Fuori scope per ora

Suoni, multi-gatto, export .exe (richiede export
templates: per ora si avvia con `run.bat`).
