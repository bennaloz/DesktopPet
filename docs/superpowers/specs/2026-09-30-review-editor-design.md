# Editor di revisione delle animazioni — design

Data: 2026-09-30. Stato: approvato in chat (strada A, pagina web; sezioni 1–2). L'utente ha chiesto di
partire subito dopo la stesura, senza un secondo giro di rilettura.

## Obiettivo

Uno strumento per **validare** le animazioni di Zaira, Bretzel e Sally e per **mostrare** a Claude come
devono muoversi. Non è uno strumento d'animazione: le clip restano generate dagli script Blender
(`tools/blender/*anim.py`), le correzioni fatte nell'editor sono un riferimento che Claude legge per
sistemare gli script. Quando una clip va bene la si approva, e Claude "registra": commit di script e GLB.

## Scelte

| Tema | Scelta |
|---|---|
| Dove | pagina web locale in `tools/review/` (`index.html`, `review.js`, `review.css`) servita da `tools/review/serve.py` (solo libreria standard Python 3.11) |
| Avvio | `review.bat` nella radice: avvia il server su `127.0.0.1:8765` e apre il browser |
| 3D | three.js da CDN (import map su `cdn.jsdelivr.net/npm/three@0.170`): `GLTFLoader`, `OrbitControls`, `TransformControls`, `CCDIKSolver` |
| Modelli | i GLB veri del gioco, `cats/<animale>/<model>` letto dal `profile.json`; nessuna copia |
| Feedback | `review/<animale>/<clip>/<AAAAMMGG-hhmmss>.json` + screenshot `…-prima.png` / `…-dopo.png` |
| Stato | `review/<animale>/stato.json` |
| Git | JSON e stato versionati; gli screenshot no (`review/**/*.png` in `.gitignore`) |
| Godot | `review/.gdignore` e `tools/review/.gdignore`, così Godot non importa PNG e JSON |
| Sally | il golden si chiama Sally: cartella `cats/sally`, `--cat=sally`, `name: "Sally"`; `assets/golden` → `assets/sally`, `PET=sally` negli script Blender |

## 1. Il server (`serve.py`)

`ThreadingHTTPServer` legato solo a `127.0.0.1`. La radice del repo è ricavata dalla posizione del file.

| Metodo e percorso | Cosa fa |
|---|---|
| `GET /` e `GET /tools/review/*` | i file della pagina |
| `GET /api/pets` | elenco delle cartelle `cats/*` con `profile.json`: `[{id, name, species, model, length_px, actions}]` (le `actions` del profilo così come sono) |
| `GET /cats/<id>/<file>` | il GLB (solo file dentro `cats/`) |
| `GET /api/state/<id>` | `stato.json` dell'animale, `{}` se manca |
| `PUT /api/state/<id>` | riscrive `stato.json` (JSON validato: oggetto con chiavi clip) |
| `POST /api/feedback/<id>/<clip>` | corpo `{json, prima, dopo}` (le due immagini come data URL PNG); scrive i tre file, risponde col nome base |
| `GET /api/feedback/<id>` | elenco dei feedback aperti (non in `risolti/`), per mostrarli nella pagina |

Sicurezza: `id` e `clip` devono rispettare `^[A-Za-z0-9_-]{1,64}$`; ogni percorso risolto deve stare
sotto `cats/`, `review/` o `tools/review/` (altrimenti 403). Corpo massimo 20 MB.

## 2. La pagina

**Barra sinistra.** Scelta dell'animale. Elenco delle clip del GLB con stato: ⚪ da vedere, ✅ approvata,
🔴 da rifare, e il bollino **nuova** se la clip è cambiata dall'ultima decisione. Per ogni clip anche le
azioni del profilo che la usano (`walk`, `trot` …) e il numero di feedback aperti.

**Scena.** Modello scalato come in Godot: la lunghezza (max tra X e Z del riquadro del modello a riposo)
diventa 1 unità di scena; il pavimento a scacchi (quadretti di 0,1 lunghezze) sta sotto i piedi. Viste:
**lato** (quella del desktop: camera ortografica di fianco, yaw 90° − `three_quarter_deg` come nel
gioco), **lato puro**, **tre quarti**, **alto**, **libera** (OrbitControls). Le clip con `show_side` nel
profilo si guardano dal lato indicato.

**Pavimento che scorre.** Per le clip di locomozione (azione con `ref_speed_px`) il pavimento scorre
all'indietro a `ref_speed_px / length_px × speed` lunghezze al secondo, cioè alla velocità a cui il
gioco muove l'animale quando suona quella clip a velocità 1. Se un piede in appoggio scivola rispetto ai
quadretti, la clip e la velocità di riferimento non sono d'accordo. Interruttore "pavimento fermo".

**Scia dei piedi.** Facoltativa: le ultime 0,6 s delle posizioni dei quattro piedi (colori diversi),
disegnate nel riferimento del pavimento.

**Tempo.** Play/pausa (spazio), fotogramma ± (← →, a 30 fps), cursore sul tempo, velocità 1× ½× ¼×,
ciclo mostrato anche come fase 0–1.

**Correzioni (solo in pausa).** Clic su una parte del corpo seleziona l'osso più vicino al punto
colpito, risalito alla "maniglia" del suo gruppo:

| Gruppo | Maniglia | Come si muove |
|---|---|---|
| zampe | bersaglio al piede (`Foot`/`Hand` o l'osso finale della catena della zampa) | trascinamento; la catena si piega con `CCDIKSolver` |
| testa, collo | `Head`, `Neck` | rotazione |
| schiena/pancia | `Spine`, `Chest` | rotazione |
| bacino | `Hips` | rotazione e spostamento |
| coda | ogni osso della catena | rotazione |
| orecchie | ogni osso della catena | rotazione |

Le catene si ricavano dai nomi delle ossa del GLB (i rig di Zaira/Sally e di Bretzel usano gli stessi
schemi: `Thigh/Shin/Foot/Toe`, `UpperArm/Forearm/Hand`, `Tail*`, `Ear*`), con un fallback che risale la
gerarchia di tre ossa dal terminale. Le ossa toccate diventano colorate; un **fantasma** semitrasparente
mostra la posa originale del fotogramma. "Annulla" (Ctrl+Z) e "Ripristina posa". Riprendere il play
scarta le correzioni non salvate (con conferma se ce ne sono).

**Salva feedback.** Campo nota + pulsante. Salva la posa com'era (screenshot "prima", fatto col
fantasma come posa piena) e quella corretta ("dopo"), dalla vista corrente.

**Decisione sulla clip.** Pulsanti "Approvata" / "Da rifare" con nota generale facoltativa.

## 3. I file

Feedback (`review/<animale>/<clip>/<base>.json`):

```json
{
  "pet": "bretzel", "clip": "Run", "time": 0.2333, "frame": 7, "phase": 0.7,
  "duration": 0.3333, "view": "lato", "note": "le zampe dietro qui devono essere già davanti",
  "summary": ["zampa post. sx (Foot.L): 5.1 cm più avanti, 2.0 cm più in alto", "Spine: +8° di arco"],
  "bones": {
    "Spine": { "before_deg": [0, 0, 12], "after_deg": [0, 0, 20] },
    "Foot.L": { "before_deg": [..], "after_deg": [..],
                "foot_before_m": [x, y, z], "foot_after_m": [x, y, z] }
  },
  "units": "gradi: rotazione locale XYZ dell'osso; m: posizione nel riferimento del modello (x avanti, y su, z lato), metri del GLB"
}
```

Il riassunto usa "avanti/indietro" lungo la direzione del muso, "su/giù", "verso/lontano dal corpo";
le rotazioni come "arco"/"inarcata al contrario" per schiena, "su/giù" per testa e coda.

Stato (`review/<animale>/stato.json`):

```json
{ "Run": { "stato": "da_rifare", "nota": "…", "impronta": "a1b2c3d4", "quando": "2026-09-30T14:02" } }
```

`impronta`: hash (FNV-1a 32 bit, esadecimale) dei valori di tutte le tracce della clip, arrotondati a
1e-4, calcolato nella pagina. Al caricamento, se l'impronta attuale differisce da quella salvata, la clip
mostra **nuova** e il suo stato visibile torna "da vedere" (il file non si tocca finché l'utente non
decide di nuovo).

## 4. Il giro di lavoro

1. L'utente rivede, approva o salva feedback.
2. Claude legge `review/<animale>/`, corregge gli script, riesporta, ricontrolla nella stessa pagina
   (browser integrato).
3. L'utente rivede solo le clip con il bollino.
4. Approvata: Claude fa commit di script, GLB e stato; i feedback risolti vanno in
   `review/<animale>/<clip>/risolti/`.

## 5. Cosa la pagina non mostra

Gli ritocchi procedurali del gioco: coda d'umore (`TailMood`, compresa la coda che scodinzola di
Sally), orecchie a molla di Bretzel, sguardo verso il cursore, rotazione di tre quarti in volo. La pagina
mostra le clip così come sono nel GLB, cioè quello che gli script producono. Scritto anche in un
riquadro "?" della pagina.

## 6. Collaudo

- `tools/review/test_serve.py` (unittest, server avviato su porta libera in un thread con una radice
  temporanea): elenco animali, lettura GLB, rifiuto di `..` e di id non validi, salvataggio feedback
  (tre file), stato letto/scritto, JSON non valido → 400.
- Funzioni pure di `review.js` che contano (riassunto delle correzioni, impronta, scala del pavimento)
  in `tools/review/lib.js`, provate con `node --test tools/review/lib.test.mjs` se `node` c'è.
- A mano da Claude nel browser integrato: le tre bestie, play/pausa, fotogramma, pavimento che scorre,
  trascinamento di una zampa, salvataggio (file creati), approvazione, bollino dopo aver cambiato una
  clip.
- Rinomina di Sally: build, 170 test e self-test del giro con `--cat=sally`.
