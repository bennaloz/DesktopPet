// Pure helpers of the review editor (no three.js), tested by lib.test.mjs with `node --test`.

/** FNV-1a 32 bit of a string, as 8 hex digits. */
export function fnv1a(str) {
  let h = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, "0");
}

/**
 * Fingerprint of a clip: its tracks' names, times and values rounded to 1e-4, so a re-export that leaves a clip
 * alone keeps its fingerprint and one that changes it does not. Tracks: [{name, times, values}].
 */
export function clipFingerprint(tracks) {
  const round = (x) => (Math.round(x * 1e4) / 1e4).toString();
  const parts = [...tracks]
    .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))
    .map((t) => `${t.name}|${Array.from(t.times, round).join(",")}|${Array.from(t.values, round).join(",")}`);
  return fnv1a(parts.join(";"));
}

/**
 * Fingerprint of the skin: the joints and weights of the skinned meshes (every value, weights rounded to 1e-3), so
 * new weights mark every clip changed even when the tracks are the same. arrays: [{index, weight}] typed arrays.
 */
export function skinFingerprint(arrays) {
  let h = 0x811c9dc5;
  const mix = (n) => {
    for (let k = 0; k < 4; k++) { h ^= (n >>> (k * 8)) & 0xff; h = Math.imul(h, 0x01000193) >>> 0; }
  };
  for (const { index, weight } of arrays) {
    for (let i = 0; i < index.length; i++) mix(index[i]);
    for (let i = 0; i < weight.length; i++) mix(Math.round(weight[i] * 1000));
  }
  return h.toString(16).padStart(8, "0");
}

/** The profile's actions that play a clip (as `anims` or `anims_right`), in profile order. */
export function clipActions(clip, profile) {
  return Object.entries(profile.actions ?? {})
    .filter(([, a]) => (a.anims ?? []).includes(clip) || (a.anims_right ?? []).includes(clip))
    .map(([name]) => name);
}

/** Clip names in profile order (each action's anims, then its right-facing ones), then the ones no action uses. */
export function orderClips(clips, profile) {
  const out = [];
  for (const a of Object.values(profile.actions ?? {}))
    for (const c of [...(a.anims ?? []), ...(a.anims_right ?? [])])
      if (clips.includes(c) && !out.includes(c)) out.push(c);
  return [...out, ...clips.filter((c) => !out.includes(c)).sort()];
}

/**
 * Locomotion of a clip: the first action using it with `ref_speed_px`. The game moves the pet at ref_speed_px when
 * the clip plays at the action's `speed`, so the floor passes under it at ref/length_px body lengths per second of
 * that playback, i.e. `rate` body lengths per second of clip time.
 */
export function locomotion(clip, profile) {
  for (const name of clipActions(clip, profile)) {
    const a = profile.actions[name];
    if (a.ref_speed_px > 0) {
      const speed = a.speed > 0 ? a.speed : 1;
      return { action: name, refSpeedPx: a.ref_speed_px, speed, rate: a.ref_speed_px / profile.length_px / speed };
    }
  }
  return null;
}

/** Playback speed the game gives a clip (the action's `speed`, 1 when none). */
export function clipSpeed(clip, profile) {
  for (const name of clipActions(clip, profile)) {
    const s = profile.actions[name].speed;
    if (s > 0) return s;
  }
  return 1;
}

/**
 * Which way the game shows a clip: +1 facing right, -1 left. `X_R` clips are the facing-right variants; their
 * plain twins are for facing left; `show_side` forces a side.
 */
export function facingFor(clip, profile) {
  if (clip.endsWith("_R")) return 1;
  for (const name of clipActions(clip, profile)) {
    const a = profile.actions[name];
    if (a.show_side === 1 || a.show_side === -1) return a.show_side;
    if ((a.anims_right ?? []).length > 0 && (a.anims ?? []).includes(clip)) return -1;
  }
  return 1;
}

const SIDE = { L: "sx", R: "dx" };
const PLAIN = {
  Hips: "bacino", Spine: "schiena", Belly: "pancia", Chest: "petto", Neck: "collo", Head: "testa", Nose: "naso",
  Tail: "coda",
};
const LEG = {
  Thigh: ["zampa post.", "coscia"], Shin: ["zampa post.", "stinco"], Foot: ["zampa post.", "piede"],
  Toe: ["zampa post.", "dita"], Scapula: ["zampa ant.", "scapola"], UpperArm: ["zampa ant.", "braccio"],
  Forearm: ["zampa ant.", "avambraccio"], Hand: ["zampa ant.", "mano"], Finger: ["zampa ant.", "dita"],
};

/** Italian name of a bone: "zampa post. sx (piede)", "coda 3", "orecchio dx 2", "testa". */
export function boneLabel(name) {
  if (PLAIN[name]) return PLAIN[name];
  let m = name.match(/^Tail(\d+)$/);
  if (m) return `coda ${m[1]}`;
  m = name.match(/^Ear(\d+)\.([LR])$/);
  if (m) return `orecchio ${SIDE[m[2]]} ${m[1]}`;
  m = name.match(/^(\w+)\.([LR])$/);
  if (m && LEG[m[1]]) return `${LEG[m[1]][0]} ${SIDE[m[2]]} (${LEG[m[1]][1]})`;
  return name;
}

const HIND = ["Thigh", "Shin", "Foot", "Toe"];
const FORE = ["UpperArm", "Forearm", "Hand", "Finger"];

/**
 * The legs of a rig, from its bone names: [{id, label, bones}] with the bones root to paw (at least two, so the
 * last one can be pulled by IK). Scapulae stay out: they are the shoulder, moved with the chest.
 */
export function legChains(names) {
  const has = new Set(names);
  const legs = [];
  for (const [kind, parts, label] of [["hind", HIND, "zampa post."], ["fore", FORE, "zampa ant."]])
    for (const side of ["L", "R"]) {
      const bones = parts.map((p) => `${p}.${side}`).filter((b) => has.has(b));
      if (bones.length >= 2) legs.push({ id: `${kind}${side}`, label: `${label} ${SIDE[side]}`, bones });
    }
  return legs;
}

/** The leg a bone belongs to, or null. */
export function legOf(bone, legs) {
  return legs.find((l) => l.bones.includes(bone)) ?? null;
}

const fmt = (x) => (Math.abs(x) >= 10 ? x.toFixed(0) : x.toFixed(1)).replace(".", ",");

function amount(x, unit, pos, neg, min) {
  if (Math.abs(x) < min) return null;
  return `${fmt(Math.abs(x))}${unit === "°" ? "" : " "}${unit} ${x > 0 ? pos : neg}`;
}

/**
 * One line saying what a correction changed, for a person and for Claude.
 * change: {label, foot?: {avanti, su, fuori} in cm, move?: {avanti, su, fuori} in cm,
 *          rot?: {su, lato, rollio} in degrees (where the bone points: up/down, left/right, and its roll)}
 */
export function describe(change) {
  const bits = [];
  for (const d of [change.foot, change.move].filter(Boolean)) {
    bits.push(
      amount(d.avanti, "cm", "più avanti", "più indietro", 0.5),
      amount(d.su, "cm", "più in alto", "più in basso", 0.5),
      amount(d.fuori ?? 0, "cm", "più in fuori", "più in dentro", 0.5),
    );
  }
  if (change.rot) {
    bits.push(
      amount(change.rot.su, "°", "più in su", "più in giù", 1),
      amount(change.rot.lato, "°", "girata a sinistra", "girata a destra", 1),
      amount(change.rot.rollio, "°", "di rollio a sinistra", "di rollio a destra", 1),
    );
  }
  const said = bits.filter(Boolean);
  return `${change.label}: ${said.length ? said.join(", ") : "ritocco minimo"}`;
}

/** Signed angle in degrees, wrapped to (-180, 180]. */
export function wrapDeg(d) {
  let x = ((d + 180) % 360 + 360) % 360 - 180;
  return x === -180 ? 180 : x;
}

/** Time snapped to a frame at `fps`, stepped by `step` frames and wrapped into [0, duration). */
export function stepFrame(t, step, duration, fps = 30) {
  const frames = Math.max(1, Math.round(duration * fps));
  const f = ((Math.round(t * fps) + step) % frames + frames) % frames;
  return f / fps;
}
