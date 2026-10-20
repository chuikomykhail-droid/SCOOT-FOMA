using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScootFoma.Rig
{
    [Serializable]
    public class ScooterBuildSettings
    {
        public float deckMass = 2.3f;
        public float barMass = 1.3f;
        public float wheelMass = 0.3f;
        public float wheelRadius = 0.055f;
        public float wheelbaseRear = -0.28f;
        public float columnZ = 0.30f;
        public float barHeight = 1.0f;
        public float gripHalfWidth = 0.25f;
        [Tooltip("Боковые опоры у деки, чтобы самокат без баланса не падал (только для тестов).")]
        public bool stabilizers;

        [Header("Колонка (привод руля)")]
        [Tooltip("Жёсткость привода колонки. 0 — руль свободен (барспин).")]
        public float steerStiffness = 20f;
        public float steerDamping = 2f;
        [Tooltip("Угол руля, который держит привод колонки, градусы.")]
        public float steerTarget;

        [Header("Колёса")]
        [Tooltip("Вязкое сопротивление вращению колеса (демпфирование сустава). Заменяет сопротивление качению.")]
        public float wheelRollingDamping;
        public float wheelFriction = 1.0f;
        public float deckFriction = 1.2f;

        [Header("Податливость колеса (промин полиуретана)")]
        [Tooltip("Жёсткость, Н/м. 0 — колесо абсолютно жёсткое. Абсолютно жёсткая система на мелком препятствии " +
                 "получает вертикальный удар v·tg(угла склона) и подлетает.")]
        public float wheelSuspensionStiffness = 400000f;
        public float wheelSuspensionDamping = 2000f;
        [Tooltip("Ход в каждую сторону, м.")]
        public float wheelSuspensionTravel = 0.006f;
    }

    public sealed class ScooterParts
    {
        public Transform Root;
        public ArticulationBody Deck;  // корень articulation
        public ArticulationBody Bar;   // руль с вилкой, revolute вокруг колонки
        public ArticulationBody FrontWheel;
        public ArticulationBody RearWheel;
        public Vector3 GripLocalL;     // в локальных координатах руля
        public Vector3 GripLocalR;
        public float DeckTopY;         // высота верха деки над землёй в позе сборки

        public ArticulationBody[] Bodies => new[] { Deck, Bar, FrontWheel, RearWheel };

        /// Угол руля относительно деки, градусы.
        public float SteerAngle => Bar.jointPosition.dofCount > 0 ? Bar.jointPosition[0] * Mathf.Rad2Deg : 0f;

        /// Задаёт колёсам угловую скорость качения (рад/с), сохраняя остальные скорости суставов.
        public void SetWheelSpin(float omega)
        {
            var velocities = new List<float>();
            var starts = new List<int>();
            Deck.GetJointVelocities(velocities);
            Deck.GetDofStartIndices(starts);
            velocities[starts[FrontWheel.index]] = omega;
            velocities[starts[RearWheel.index]] = omega;
            Deck.SetJointVelocities(velocities);
        }
    }

    /// Самокат — отдельная articulation: дека (корень) → руль (revolute, бесконечный) → переднее колесо;
    /// дека → заднее колесо. Колёса — настоящие вращающиеся звенья: трение PhysX у катящегося тела честное,
    /// а приведённые координаты не дают шарнирам колёс разойтись под весом райдера (спайк S3).
    /// Хват руки — Joint на кисти-Rigidbody (см. RiderScooterCoupling): на ArticulationBody Joint не вешается.
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
            var wheelMat = Material("Wheel", s.wheelFriction);

            // Дека — корень
            var deckGo = Node("Deck", container, layer, pose.position + pose.rotation * new Vector3(0f, deckCenterY, 0f), pose.rotation);
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
            parts.Deck = Link(deckGo, s.deckMass);

            // Руль с вилкой: звено в точке колонки на уровне верха деки, revolute вокруг вертикали рига
            float barBaseY = parts.DeckTopY;
            var barGo = Node("Bar", deckGo.transform, layer, pose.position + pose.rotation * new Vector3(0f, barBaseY, s.columnZ), pose.rotation);
            float wheelLocalY = r - barBaseY;
            float topLocalY = s.barHeight - barBaseY;
            AddCapsule(barGo, "Column", new Vector3(0f, (wheelLocalY + topLocalY) * 0.5f, 0f), 0.016f, topLocalY - wheelLocalY, 1, deckMat);
            AddCapsule(barGo, "Handlebar", new Vector3(0f, topLocalY, 0f), 0.012f, s.gripHalfWidth * 2f + 0.08f, 0, deckMat);
            parts.GripLocalL = new Vector3(-s.gripHalfWidth, topLocalY, 0f);
            parts.GripLocalR = new Vector3(s.gripHalfWidth, topLocalY, 0f);
            parts.Bar = Link(barGo, s.barMass);
            // Ось X якоря = вертикаль колонки. Без лимитов: барспин и тейлвип крутят колонку бесконечно.
            Revolute(parts.Bar, Quaternion.Euler(0f, 0f, 90f), new ArticulationDrive
            {
                driveType = ArticulationDriveType.Force,
                stiffness = s.steerStiffness,
                damping = s.steerDamping,
                forceLimit = float.MaxValue,
                target = s.steerTarget,
            });

            // Колёса: ось X якоря = ось колеса (X рига)
            parts.FrontWheel = Wheel("FrontWheel", barGo.transform, layer, pose.position + pose.rotation * new Vector3(0f, r, s.columnZ), pose.rotation, s, wheelMat);
            parts.RearWheel = Wheel("RearWheel", deckGo.transform, layer, pose.position + pose.rotation * new Vector3(0f, r, s.wheelbaseRear), pose.rotation, s, wheelMat);

            IgnoreSelfCollisions(deckGo.transform);
            return parts;
        }

        static ArticulationBody Wheel(string name, Transform parent, int layer, Vector3 position, Quaternion rotation,
            ScooterBuildSettings s, PhysicsMaterial mat)
        {
            if (s.wheelSuspensionStiffness > 0f)
            {
                // Ступица на призматическом суставе вдоль вертикали рига: моделирует промин колеса.
                var hubGo = Node(name + "Hub", parent, layer, position, rotation);
                var hub = Link(hubGo, 0.05f);
                hub.automaticInertiaTensor = false;
                hub.inertiaTensor = Vector3.one * 1e-4f;
                hub.jointType = ArticulationJointType.PrismaticJoint;
                hub.matchAnchors = true;
                hub.anchorPosition = Vector3.zero;
                hub.anchorRotation = Quaternion.Euler(0f, 0f, 90f); // ось X якоря = вертикаль
                hub.linearLockX = ArticulationDofLock.LimitedMotion;
                hub.xDrive = new ArticulationDrive
                {
                    driveType = ArticulationDriveType.Force,
                    stiffness = s.wheelSuspensionStiffness,
                    damping = s.wheelSuspensionDamping,
                    forceLimit = float.MaxValue,
                    lowerLimit = -s.wheelSuspensionTravel,
                    upperLimit = s.wheelSuspensionTravel,
                };
                parent = hubGo.transform;
            }

            var go = Node(name, parent, layer, position, rotation);
            var c = go.AddComponent<SphereCollider>();
            c.radius = s.wheelRadius;
            c.sharedMaterial = mat;
            var ab = Link(go, s.wheelMass);
            ab.maxAngularVelocity = 400f;
            ab.maxJointVelocity = 400f;
            Revolute(ab, Quaternion.identity, new ArticulationDrive
            {
                driveType = ArticulationDriveType.Force,
                stiffness = 0f,
                damping = s.wheelRollingDamping,
                forceLimit = float.MaxValue,
            });
            return ab;
        }

        /// Детали самоката не должны сталкиваться между собой: вращающееся колесо цепляет деку
        /// в пределах contact offset и идёт вразнос.
        static void IgnoreSelfCollisions(Transform root)
        {
            var colliders = root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                if (colliders[i].attachedArticulationBody != colliders[j].attachedArticulationBody)
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);
        }

        static GameObject Node(string name, Transform parent, int layer, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        static ArticulationBody Link(GameObject go, float mass)
        {
            var ab = go.AddComponent<ArticulationBody>();
            ab.mass = mass;
            ab.linearDamping = 0f;
            ab.angularDamping = 0f;
            ab.jointFriction = 0f;
            ab.sleepThreshold = 0f;
            ab.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return ab;
        }

        static void Revolute(ArticulationBody ab, Quaternion anchorRotation, ArticulationDrive drive)
        {
            ab.jointType = ArticulationJointType.RevoluteJoint;
            ab.twistLock = ArticulationDofLock.FreeMotion;
            ab.matchAnchors = true;
            ab.anchorPosition = Vector3.zero;
            ab.anchorRotation = anchorRotation;
            ab.xDrive = drive;
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
    }
}
