// The room's things, as Props.cs builds them in the game: the bowl (kibble, or hay for the rabbit), the cat's perch,
// a treat, and the shelf on the wall the pets can jump up onto. Room units; three's y is up.
import * as THREE from "three";

const mat = (c, r = 0.85) => new THREE.MeshStandardMaterial({ color: c, roughness: r });

export function bowl(hay) {
  const g = new THREE.Group();
  const shape = new THREE.Mesh(new THREE.CylinderGeometry(30, 22, 18, 28, 1, true), mat(0xb03a2e, 0.5));
  shape.material.side = THREE.DoubleSide;
  shape.position.y = 9;
  const base = new THREE.Mesh(new THREE.CylinderGeometry(22, 22, 3, 28), mat(0x8f2d24, 0.5));
  base.position.y = 1.5;
  g.add(shape, base);
  const food = new THREE.Group();
  if (hay) {
    const straw = [0xd8c27a, 0xc9b05e, 0xe6d391].map((c) => mat(c, 1));
    const mound = new THREE.Mesh(new THREE.SphereGeometry(24, 16, 8, 0, Math.PI * 2, 0, Math.PI / 2), straw[0]);
    mound.scale.y = 0.5; mound.position.y = 14;
    food.add(mound);
    let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let i = 0; i < 36; i++) {
      const len = 16 + rnd() * 18;
      const s = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.7, len, 4), straw[i % 3]);
      const a = rnd() * Math.PI * 2, r = rnd() * 18, tilt = THREE.MathUtils.degToRad(15 + rnd() * 50);
      s.position.set(Math.cos(a) * r, 18 + Math.cos(tilt) * len / 2, Math.sin(a) * r);
      s.rotation.set(Math.sin(a) * tilt, 0, -Math.cos(a) * tilt);
      food.add(s);
    }
  } else {
    const kib = mat(0x8a5a2b, 0.9);
    for (let i = 0; i < 22; i++) {
      const k = new THREE.Mesh(new THREE.SphereGeometry(4.5, 8, 6), kib);
      const a = i * 2.4, r = 3 + (i % 5) * 4;
      k.position.set(Math.cos(a) * r, 15 + (i % 3) * 1.5, Math.sin(a) * r);
      food.add(k);
    }
  }
  g.add(food);
  g.userData.food = food;
  return g;
}

export const PERCH_HEIGHT = 230, PERCH_TOP = 124;

export function perch() {
  const g = new THREE.Group();
  const wood = mat(0x9e7854), rope = mat(0xd4bd8c, 1), cushion = mat(0x6b8cb8, 1);
  const add = (geo, m, y) => { const o = new THREE.Mesh(geo, m); o.position.y = y; g.add(o); };
  add(new THREE.BoxGeometry(110, 12, 70), wood, 6);
  add(new THREE.CylinderGeometry(11, 11, PERCH_HEIGHT - 12, 16), rope, 12 + (PERCH_HEIGHT - 12) / 2);
  add(new THREE.BoxGeometry(PERCH_TOP, 12, 80), wood, PERCH_HEIGHT - 6);
  add(new THREE.CylinderGeometry(44, 48, 10, 24), cushion, PERCH_HEIGHT + 5);
  return g;
}

export function treat() {
  const g = new THREE.Group();
  const m = mat(0x8c5429, 0.9);
  const a = new THREE.Mesh(new THREE.SphereGeometry(7, 10, 8), m); a.scale.y = 0.8; a.position.y = 5;
  const b = new THREE.Mesh(new THREE.SphereGeometry(5, 10, 8), mat(0xa86b33, 0.9)); b.position.set(6, 4, 2);
  g.add(a, b);
  return g;
}

/** A plank on the wall, as wide as the shelf rectangle (left, top, right, bottom in room units). */
export function shelf() {
  const g = new THREE.Group();
  const plank = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 90), mat(0xa47b56, 0.7));
  const lip = new THREE.Mesh(new THREE.BoxGeometry(1, 3, 92), mat(0x8c6644, 0.7));
  g.add(plank, lip);
  g.userData.place = (l, t, r, b) => {
    plank.scale.set(r - l, b - t, 1);
    plank.position.set((l + r) / 2, -(t + b) / 2, -20);
    lip.scale.set(r - l + 4, 1, 1);
    lip.position.set((l + r) / 2, -t - 1.5, -20);
  };
  return g;
}
