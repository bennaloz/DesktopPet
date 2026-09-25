# Pipeline Blender di Zaira

Blender 5.2 portable in `C:\develop\personal\tools\`. Sorgente: GLB esportato da Tripo in
`C:\develop\personal\_assets\zaira\` (lo scheletro di Tripo viene buttato).

1. `prep.py` – importa, raddrizza (muso verso -Y), centra, piedi a z=0 → `work/mesh.blend`
2. `rig.py` – decimazione a ~25k facce, scheletro da gatto, pesi via proxy voxel,
   pesi della coda procedurali, stacco delle facce che incollavano coda e sedere → `work/rig.blend`
3. `anim.py` – animazioni procedurali (IK planare per le zampe) → `work/anim.blend`
4. `export.py` – texture a 2K, GLB con tutte le azioni → `cats/zaira/zaira.glb`

Controllo visivo: `sheet.py` + `tile.py` (foglio di fotogrammi per ogni animazione).

    blender.exe --background --python rig.py
