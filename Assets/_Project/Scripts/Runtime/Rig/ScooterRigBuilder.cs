using System;
using UnityEngine;

namespace ScootFoma.Rig
{
    public enum WheelModel
    {
        /// Колесо — отдельный Rigidbody на шарнире, трение PhysX.
        PhysicalBody,
        /// Колесо — коллайдер без трения на деке/руле, анизотропное трение считает WheelFrictionDriver.
        CustomFriction,
        /// Колесо — коллайдер с трением на деке/руле; качение через contact modification (RollingContactWheels).
        RollingContact,
    }

    [Serializable]
    public class ScooterBuildSettings
    {
        public WheelModel wheelModel = WheelModel.PhysicalBody;
        public float deckMass = 2.6f;
        public float barMass = 1.6f;
        [Tooltip("Только для PhysicalBody: масса каждого колеса. В CustomFriction колесо входит в массу деки/руля.")]
        public float wheelMass = 0.3f;
        public float wheelRadius = 0.055f;
        public float wheelbaseRear = -0.28f;
        public float columnZ = 0.30f;
        public float barHeight = 1.0f;
        public float gripHalfWidth = 0.25f;
        [Tooltip("Боковые опоры у деки, чтобы самокат без баланса не падал (только для тестов).")]
        public bool stabilizers;
        public float steerSpring = 20f;
        public float steerDamper = 2f;
        [Tooltip("Угол руля, который держит пружина колонки, градусы.")]
        public float steerTarget;
        public float wheelFriction = 1.0f;
        public float deckFriction = 1.2f;
    }

    public sealed class ScooterParts
    {
        public Transform Root;
        public Rigidbody Deck;
        public Rigidbody Bar;
        public HingeJoint Column;
        public SphereCollider FrontWheelCollider;
        public SphereCollider RearWheelCollider;
        public Rigidbody FrontWheel; // null в CustomFriction
        public Rigidbody RearWheel;  // null в CustomFriction
        public Vector3 GripLocalL;   // в локальных координатах руля
        public Vector3 GripLocalR;
        public float DeckTopY;       // высота верха деки над землёй в позе сборки

        public Rigidbody[] Bodies => FrontWheel != null
            ? new[] { Deck, Bar, FrontWheel, RearWheel }
            : new[] { Deck, Bar };
    }

    /// Самокат из Rigidbody: Joint требует Rigidbody (на ArticulationBody его не повесить),
    /// а хвату руки нужен Joint. Поэтому самокат — Rigidbody, райдер — ArticulationBody.
    public static class ScooterRigBuilder
    {
        const float DeckThickness = 0.03f;
        const float DeckWidth = 0.12f;

        public static ScooterParts Build(Transform container, Pose pose, ScooterBuildSettings s, int layer)
        {
            var parts = new ScooterParts { Root = container };
            float r = s.wheelRadius;
            float deckCenterY = r + 0.03f;
            parts.DeckTopY = deckCenterY + DeckThickness * 0.5f;

            var deckMat = Material("Deck", s.deckFriction);
            var wheelMat = s.wheelModel == WheelModel.CustomFriction
                ? Frictionless("WheelFrictionless")
                : Material("Wheel", s.wheelFriction);

            // Дека
            var deckGo = Node("Deck", container, layer, pose, new Vector3(0f, deckCenterY, 0f));
            // Дека между колёсами с зазором 5 мм, шея поднята над передним колесом к рулевому стакану.
            float deckRear = s.wheelbaseRear + r + 0.005f;
            float deckFront = s.columnZ - r - 0.005f;
            AddBox(deckGo, "DeckBoard", new Vector3(0f, 0f, (deckRear + deckFront) * 0.5f),
                new Vector3(DeckWidth, DeckThickness, deckFront - deckRear), deckMat);
            float neckBottomY = 2f * r + 0.02f;
            const float neckHeight = 0.03f;
            float neckLocalY = neckBottomY + neckHeight * 0.5f - deckCenterY;
            float neckStart = deckFront - 0.04f;
            AddBox(deckGo, "Neck", new Vector3(0f, neckLocalY, (neckStart + s.columnZ) * 0.5f),
                new Vector3(0.05f, neckHeight, s.columnZ - neckStart), deckMat);
            if (s.stabilizers)
            {
                float y = -deckCenterY + 0.012f;
                AddBox(deckGo, "StabilizerL", new Vector3(-0.35f, y, -0.02f), new Vector3(0.05f, 0.02f, 0.12f), deckMat);
                AddBox(deckGo, "StabilizerR", new Vector3(0.35f, y, -0.02f), new Vector3(0.05f, 0.02f, 0.12f), deckMat);
            }
            parts.Deck = Body(deckGo, s.deckMass);

            // Руль с вилкой: тело в точке колонки на уровне верха деки
            float barBaseY = parts.DeckTopY;
            var barGo = Node("Bar", container, layer, pose, new Vector3(0f, barBaseY, s.columnZ));
            float wheelLocalY = r - barBaseY;
            float topLocalY = s.barHeight - barBaseY;
            AddCapsule(barGo, "Column", new Vector3(0f, (wheelLocalY + topLocalY) * 0.5f, 0f), 0.016f, topLocalY - wheelLocalY, 1, deckMat);
            AddCapsule(barGo, "Handlebar", new Vector3(0f, topLocalY, 0f), 0.012f, s.gripHalfWidth * 2f + 0.08f, 0, deckMat);
            parts.GripLocalL = new Vector3(-s.gripHalfWidth, topLocalY, 0f);
            parts.GripLocalR = new Vector3(s.gripHalfWidth, topLocalY, 0f);
            parts.Bar = Body(barGo, s.barMass);

            var column = barGo.AddComponent<HingeJoint>();
            column.connectedBody = parts.Deck;
            column.anchor = Vector3.zero;
            column.axis = Vector3.up;
            column.useLimits = false; // барспин и тейлвип: колонка крутится бесконечно
            column.useSpring = s.steerSpring > 0f;
            column.spring = new JointSpring { spring = s.steerSpring, damper = s.steerDamper, targetPosition = s.steerTarget };
            column.enableCollision = false;
            parts.Column = column;

            // Колёса
            Vector3 frontWheelPos = new Vector3(0f, r, s.columnZ);
            Vector3 rearWheelPos = new Vector3(0f, r, s.wheelbaseRear);
            if (s.wheelModel != WheelModel.PhysicalBody)
            {
                parts.FrontWheelCollider = AddWheelCollider(barGo, "FrontWheel", barGo.transform.InverseTransformPoint(pose.position + pose.rotation * frontWheelPos), r, wheelMat);
                parts.RearWheelCollider = AddWheelCollider(deckGo, "RearWheel", deckGo.transform.InverseTransformPoint(pose.position + pose.rotation * rearWheelPos), r, wheelMat);
            }
            else
            {
                parts.FrontWheel = WheelBody("FrontWheel", container, layer, pose, frontWheelPos, s, wheelMat, parts.Bar, out parts.FrontWheelCollider);
                parts.RearWheel = WheelBody("RearWheel", container, layer, pose, rearWheelPos, s, wheelMat, parts.Deck, out parts.RearWheelCollider);
            }

            IgnoreSelfCollisions(container);
            return parts;
        }

        /// Детали самоката связаны шарнирами и не должны сталкиваться между собой. Иначе вращающееся
        /// колесо цепляет деку в пределах contact offset и идёт вразнос.
        static void IgnoreSelfCollisions(Transform container)
        {
            var colliders = container.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                if (colliders[i].attachedRigidbody != colliders[j].attachedRigidbody)
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        static Rigidbody WheelBody(string name, Transform container, int layer, Pose pose, Vector3 localPos,
            ScooterBuildSettings s, PhysicsMaterial mat, Rigidbody attachTo, out SphereCollider collider)
        {
            var go = Node(name, container, layer, pose, localPos);
            collider = go.AddComponent<SphereCollider>();
            collider.radius = s.wheelRadius;
            collider.sharedMaterial = mat;
            var rb = Body(go, s.wheelMass);
            rb.maxAngularVelocity = 400f;
            var hinge = go.AddComponent<HingeJoint>();
            hinge.connectedBody = attachTo;
            hinge.anchor = Vector3.zero;
            hinge.axis = Vector3.right;
            hinge.enableCollision = false;
            return rb;
        }

        static SphereCollider AddWheelCollider(GameObject owner, string name, Vector3 localPos, float radius, PhysicsMaterial mat)
        {
            var go = new GameObject(name) { layer = owner.layer };
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = localPos;
            var c = go.AddComponent<SphereCollider>();
            c.radius = radius;
            c.sharedMaterial = mat;
            return c;
        }

        static GameObject Node(string name, Transform container, int layer, Pose pose, Vector3 localPos)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(container, false);
            go.transform.SetPositionAndRotation(pose.position + pose.rotation * localPos, pose.rotation);
            return go;
        }

        static Rigidbody Body(GameObject go, float mass)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.01f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.None; // визуал интерполирует PoseInterpolator
            rb.maxAngularVelocity = 100f;
            return rb;
        }

        static void AddBox(GameObject owner, string name, Vector3 center, Vector3 size, PhysicsMaterial mat)
        {
            var go = new GameObject(name) { layer = owner.layer };
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = center;
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            c.sharedMaterial = mat;
        }

        static void AddCapsule(GameObject owner, string name, Vector3 center, float radius, float height, int direction, PhysicsMaterial mat)
        {
            var go = new GameObject(name) { layer = owner.layer };
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = center;
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = radius;
            c.height = height;
            c.direction = direction;
            c.sharedMaterial = mat;
        }

        internal static PhysicsMaterial Material(string name, float friction) => new PhysicsMaterial(name)
        {
            staticFriction = friction,
            dynamicFriction = friction,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };

        static PhysicsMaterial Frictionless(string name) => new PhysicsMaterial(name)
        {
            staticFriction = 0f,
            dynamicFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };
    }
}
