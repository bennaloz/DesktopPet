// The animal on the phone: what CatVisual.cs does in the game, with three.js. Loads the pet's GLB, scales it to its
// length on screen, plays the profile's clips for the brain's actions (with the same fallbacks), turns it to face
// where it goes, hangs it when held, tips it in the air, keeps the clip's pace to its real speed, and opens a dog's
// mouth when she pants. Units are the room's (CSS pixels / scale); three's y is up, the room's y is down.
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

export const HELD_ROLL_DEG = 60, CLIMB_ROLL_DEG = 78;

// CatProfile.Resolve: the clip for an action, or a similar one when the model lacks it
const FALLBACK = {
  sleep: "sit", purr: "sit", meow: "idle", held: "idle", land: "idle", prejump: "idle", aim: "prejump", fall: "jump",
  run: "walk", lope: "run", trot: "walk", stalk: "sit", wiggle: "stalk", swat: "idle", sit: "idle", loaf: "crouch",
  tuck: "loaf", crouch: "sit", liedown: "crouch", climb: "walk", eat: "idle", hop: "walk", petted: "purr",
  flop: "flopsleep", flopsleep: "sleep", binky: "jump", thump: "idle", groom: "sit", bark: "meow",
};

export class Pet {
  constructor(profile) {
    this.profile = profile;
    this.root = new THREE.Group();          // at the body's feet
    this.pivot = new THREE.Group();          // yaw, roll
    this.root.add(this.pivot);
    this.yaw = 0; this.roll = 0; this.heldSpin = 0; this.time = 0;
    this.action = ""; this.clip = null; this.side = 0; this.current = null;
    this.size = { w: profile.length_px, h: profile.length_px * 0.6 };
    this.clipPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 1e9);
  }

  async load(url) {
    const gltf = await new GLTFLoader().loadAsync(url);
    this.model = gltf.scene;
    this.pivot.add(this.model);
    this.bones = new Map();
    this.model.traverse((o) => {
      if (o.isBone) this.bones.set(o.name, o);
      if (o.isMesh) {
        o.frustumCulled = false;
        for (const m of [o.material].flat()) m.clippingPlanes = [this.clipPlane];
      }
    });
    this.rest = new Map([...this.bones].map(([n, b]) => [n, b.quaternion.clone()]));
    this.mixer = new THREE.AnimationMixer(this.model);
    this.clips = new Map();
    for (const c of gltf.animations) { this.fillRest(c); this.clips.set(c.name.toLowerCase(), c); }
    this.fit();
    const jaw = this.profile.pant_bone && this.bones.get(this.profile.pant_bone);
    this.jaw = jaw || null;
    this.pantShown = 0;
    this.play("idle", 1);
  }

  // Exporters drop tracks of bones a clip leaves at rest: without them a bone keeps the previous clip's pose.
  fillRest(clip) {
    const have = new Set(clip.tracks.map((t) => t.name));
    for (const [name, b] of this.bones) {
      const q = `${b.name}.quaternion`, p = `${b.name}.position`;
      if (!have.has(q)) clip.tracks.push(new THREE.QuaternionKeyframeTrack(q, [0], b.quaternion.toArray()));
      if (!have.has(p)) clip.tracks.push(new THREE.VectorKeyframeTrack(p, [0], b.position.toArray()));
    }
  }

  // CatVisual.FitToLength: the longer of X and Z becomes length_px, the feet on the origin; props held (Prop_*) left out
  fit() {
    const box = new THREE.Box3();
    this.model.updateMatrixWorld(true);
    this.model.traverse((o) => { if (o.isMesh && !o.name.startsWith("Prop_")) box.expandByObject(o, true); });
    const size = box.getSize(new THREE.Vector3());
    const s = this.profile.length_px / Math.max(size.x, size.z, 1e-4);
    this.model.scale.multiplyScalar(s);
    this.model.updateMatrixWorld(true);
    box.makeEmpty();
    this.model.traverse((o) => { if (o.isMesh && !o.name.startsWith("Prop_")) box.expandByObject(o, true); });
    const c = box.getCenter(new THREE.Vector3());
    this.model.position.sub(new THREE.Vector3(c.x, box.min.y, c.z));
    this.size = { w: this.profile.length_px, h: box.max.y - box.min.y };
  }

  resolve(action) {
    for (let a = action, n = 0; a && n < 8; a = FALLBACK[a], n++) {
      const c = this.profile.actions[a];
      if (c && c.anims && c.anims.length) return c;
    }
    return null;
  }

  play(action, facing) {
    const side = facing >= 0 ? 1 : -1;
    if (action === this.action) {
      if (this.clip && (this.clip.anims_right || []).length && side !== this.side) this.start(this.clip, side, true);
      return;
    }
    const clip = this.resolve(action);
    if (!clip) return;
    this.action = action; this.clip = clip;
    this.variant = Math.floor(Math.random() * clip.anims.length);
    this.start(clip, side, false);
  }

  start(clip, side, keepTime) {
    this.side = side;
    const list = side > 0 && (clip.anims_right || []).length === clip.anims.length ? clip.anims_right : clip.anims;
    const c = this.clips.get(list[this.variant].toLowerCase()) || this.clips.get(clip.anims[this.variant].toLowerCase());
    if (!c) return;
    const next = this.mixer.clipAction(c);
    const t = keepTime && this.current ? this.current.time : 0;
    next.reset();
    next.setLoop(clip.loop === false ? THREE.LoopOnce : THREE.LoopRepeat, Infinity);
    next.clampWhenFinished = clip.loop === false;
    next.time = Math.min(t, c.duration);
    next.play();
    if (this.current && this.current !== next) this.current.crossFadeTo(next, keepTime ? 0.3 : (clip.blend ?? 0.18), false);
    this.current = next;
  }

  /** Per frame: s = Api.State() (see Api.cs), dt in seconds. */
  animate(dt, s) {
    this.time += dt;
    const [x, y, vx, , mode, facingIn] = s;
    const held = mode === 2, climbing = mode === 3, air = mode === 1;
    let facing = facingIn;
    if (this.clip && (this.clip.show_side === 1 || this.clip.show_side === -1)) facing = this.clip.show_side;
    const p = this.profile;
    let target = held ? this.heldSpin : facing * (90 - (p.three_quarter_deg ?? 25) * (1 - (air ? s[21] : 0)));
    target += p.yaw_offset_deg ?? 0;
    this.yaw += (target - this.yaw) * Math.min(1, dt * (held ? 20 : 10));
    const pitch = held ? HELD_ROLL_DEG : climbing ? CLIMB_ROLL_DEG : air ? s[20] : 0;
    this.roll += (pitch - this.roll) * Math.min(1, dt * 12);
    const sway = held ? 6 * Math.sin(this.time * 2.2) : 0;
    const headSide = Math.sin(THREE.MathUtils.degToRad(this.yaw)) >= 0 ? 1 : -1;
    const qRoll = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 0, 1),
      THREE.MathUtils.degToRad((this.roll + sway) * headSide));
    const qYaw = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), THREE.MathUtils.degToRad(this.yaw));
    this.pivot.quaternion.copy(qRoll.multiply(qYaw));

    if (this.current && this.clip) {
      let speed = this.clip.speed ?? 1;
      const ground = climbing ? 170 : Math.abs(vx);
      if (this.clip.ref_speed_px > 0 && ground > 1) speed *= Math.min(2.2, Math.max(0.5, ground / this.clip.ref_speed_px));
      this.current.setEffectiveTimeScale(speed);
    }
    this.mixer.update(dt);

    // panting: the jaw back towards the model's open mouth (PantModifier.cs)
    if (this.jaw) {
      const want = s[13];
      this.pantShown += Math.max(-dt * 1.5, Math.min(dt * 1.5, want - this.pantShown));
      if (this.pantShown > 0.001) {
        const w = this.pantShown * (1 - 0.18 * (0.5 + 0.5 * Math.sin(Math.PI * 2 * 3 * this.time)));
        this.jaw.quaternion.slerp(this.rest.get(this.jaw.name), w);
      }
    }

    this.root.position.set(x, -y, 0);
    // standing on something, nothing of it is drawn below it (the game's floor clip)
    this.clipPlane.constant = mode === 0 ? y + 0.5 : 1e9;
  }
}
