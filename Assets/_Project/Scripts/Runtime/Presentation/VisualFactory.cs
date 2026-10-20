using UnityEngine;

namespace ScootFoma.Presentation
{
    /// Создаёт визуальные примитивы по коллайдерам физического тела.
    /// Визуал не является дочерним объектом физики и двигается через PoseInterpolator.
    public static class VisualFactory
    {
        public static Transform MirrorBody(Component body, Transform visualRoot, PoseInterpolator interpolator, Material material)
        {
            var visual = new GameObject(body.name + " (visual)").transform;
            visual.SetParent(visualRoot, false);
            body.transform.GetPositionAndRotation(out var p, out var r);
            visual.SetPositionAndRotation(p, r);

            foreach (var col in body.GetComponentsInChildren<Collider>())
            {
                if (!BelongsTo(col, body)) continue;
                var prim = CreatePrimitive(col);
                if (prim == null) continue;
                prim.transform.SetParent(visual, true);
                if (material != null) prim.GetComponent<Renderer>().sharedMaterial = material;
            }

            interpolator.Track(body.transform, visual);
            return visual;
        }

        static bool BelongsTo(Collider col, Component body)
        {
            if (body is Rigidbody rb) return col.attachedRigidbody == rb;
            if (body is ArticulationBody ab) return col.attachedArticulationBody == ab;
            return false;
        }

        static GameObject CreatePrimitive(Collider col)
        {
            var t = col.transform;
            GameObject go;
            switch (col)
            {
                case BoxCollider box:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.SetPositionAndRotation(t.TransformPoint(box.center), t.rotation);
                    go.transform.localScale = Vector3.Scale(box.size, t.lossyScale);
                    break;
                case SphereCollider sphere:
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.SetPositionAndRotation(t.TransformPoint(sphere.center), t.rotation);
                    go.transform.localScale = Vector3.one * (sphere.radius * 2f * MaxAbs(t.lossyScale));
                    break;
                case CapsuleCollider capsule:
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    // Примитив капсулы вытянут по Y; повернуть под направление коллайдера.
                    Quaternion axis = capsule.direction == 0 ? Quaternion.Euler(0f, 0f, 90f)
                        : capsule.direction == 2 ? Quaternion.Euler(90f, 0f, 0f)
                        : Quaternion.identity;
                    go.transform.SetPositionAndRotation(t.TransformPoint(capsule.center), t.rotation * axis);
                    float d = capsule.radius * 2f;
                    go.transform.localScale = new Vector3(d, Mathf.Max(capsule.height, d) * 0.5f, d);
                    break;
                default:
                    return null;
            }
            go.name = col.name + " (mesh)";
            Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
    }
}
