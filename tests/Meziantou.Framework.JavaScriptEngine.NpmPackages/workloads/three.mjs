// three.js: the math classes (vectors, matrices, quaternions, Euler angles, rays, boxes, curves), geometry generation
// and the scene graph, without WebGL
import * as THREE from "three";

const round = value => Math.round(value * 1e9) / 1e9;
const roundAll = values => Array.from(values, round);

export default () => {
  const matrix = new THREE.Matrix4().makeRotationY(Math.PI / 3).multiply(new THREE.Matrix4().makeTranslation(1, 2, 3)).scale(new THREE.Vector3(2, 2, 2));
  const position = new THREE.Vector3();
  const quaternion = new THREE.Quaternion();
  const scale = new THREE.Vector3();
  matrix.decompose(position, quaternion, scale);

  const scene = new THREE.Scene();
  const group = new THREE.Group();
  group.position.set(1, 0, 0);
  group.rotation.set(0, Math.PI / 2, 0);
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(1, 2, 3, 2, 2, 2), new THREE.MeshBasicMaterial());
  mesh.position.set(0, 1, 0);
  mesh.name = "box";
  group.add(mesh);
  scene.add(group);
  scene.updateMatrixWorld(true);

  const camera = new THREE.PerspectiveCamera(60, 16 / 9, 0.1, 100);
  camera.position.set(0, 2, 10);
  camera.lookAt(0, 0, 0);
  camera.updateMatrixWorld();
  const raycaster = new THREE.Raycaster();
  raycaster.setFromCamera(new THREE.Vector2(0, 0), camera);

  const curve = new THREE.CatmullRomCurve3([new THREE.Vector3(0, 0, 0), new THREE.Vector3(1, 2, 0), new THREE.Vector3(3, 1, 1), new THREE.Vector3(4, 0, 0)]);
  const torus = new THREE.TorusKnotGeometry(1, 0.3, 32, 8);
  torus.computeBoundingSphere();
  const sphere = new THREE.SphereGeometry(1, 8, 6).toNonIndexed();
  sphere.computeVertexNormals();
  const shape = new THREE.Shape([new THREE.Vector2(0, 0), new THREE.Vector2(2, 0), new THREE.Vector2(2, 2), new THREE.Vector2(1, 3), new THREE.Vector2(0, 2)]);
  const extruded = new THREE.ExtrudeGeometry(shape, { depth: 1, bevelEnabled: true, bevelSegments: 2 });

  return JSON.stringify({
    matrix: roundAll(matrix.elements),
    inverse: roundAll(matrix.clone().invert().elements),
    decomposed: [roundAll(position.toArray()), roundAll(quaternion.toArray()), roundAll(scale.toArray())],
    euler: roundAll(new THREE.Euler().setFromQuaternion(quaternion, "ZYX").toArray().slice(0, 3)),
    slerp: roundAll(new THREE.Quaternion().slerpQuaternions(new THREE.Quaternion(), quaternion, 0.25).toArray()),
    world: roundAll(mesh.getWorldPosition(new THREE.Vector3()).toArray()),
    box: [roundAll(new THREE.Box3().setFromObject(mesh).min.toArray()), roundAll(new THREE.Box3().setFromObject(mesh).max.toArray())],
    projected: roundAll(new THREE.Vector3(1, 1, 1).project(camera).toArray()),
    intersections: raycaster.intersectObjects(scene.children, true).map(hit => [hit.object.name, round(hit.distance), hit.faceIndex]),
    curve: curve.getSpacedPoints(4).map(point => roundAll(point.toArray())),
    curveLength: round(curve.getLength()),
    geometry: [torus.attributes.position.count, torus.index.count, round(torus.boundingSphere.radius), sphere.attributes.position.count, roundAll(sphere.attributes.normal.array.slice(0, 6)), extruded.attributes.position.count],
    color: [new THREE.Color("hsl(200, 50%, 40%)").getHexString(), new THREE.Color(0x336699).convertSRGBToLinear().toArray().map(round), new THREE.Color().setHSL(0.3, 0.6, 0.5, THREE.SRGBColorSpace).getStyle()],
    math: [round(THREE.MathUtils.smootherstep(0.3, 0, 1)), round(THREE.MathUtils.damp(0, 10, 4, 0.1)), new THREE.Plane(new THREE.Vector3(0, 1, 0), -1).distanceToPoint(new THREE.Vector3(0, 5, 0)), new THREE.Triangle(new THREE.Vector3(), new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 1, 0)).getArea()],
    json: Object.keys(scene.toJSON()),
  });
};
