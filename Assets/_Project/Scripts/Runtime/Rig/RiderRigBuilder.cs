using System;
using System.Collections.Generic;
using ScootFoma.Control;
using UnityEngine;

namespace ScootFoma.Rig
{
    [Serializable]
    public class DriveSettings
    {
        public ArticulationDriveType type = ArticulationDriveType.Force;
        public float stiffness = 2000f;
        public float damping = 200f;
        public float forceLimit = 3000f;

        public DriveSettings() { }
        public DriveSettings(float stiffness, float damping, float forceLimit)
        {
            this.stiffness = stiffness;
            this.damping = damping;
            this.forceLimit = forceLimit;
        }
    }

    [Serializable]
    public class RiderBuildSettings
    {
        [Header("Массы, кг")]
        public float pelvisMass = 12f;
        public float torsoMass = 32f;
        public float upperArmMass = 2.2f;
        public float forearmMass = 1.8f;
        public float thighMass = 8f;
        public float shinMass = 3.6f;
        public float footMass = 1.0f;

        [Header("Длины, м")]
        public float upperArmLength = 0.30f;
        public float forearmLength = 0.32f;
        public float thighLength = 0.42f;
        public float shinLength = 0.42f;

        [Header("Поза сборки (координаты рига)")]
        public Vector3 pelvis = new Vector3(0f, 0.98f, -0.04f);
        public float hipHalfWidth = 0.09f;
        public float shoulderHalfWidth = 0.19f;
        public float shoulderHeightAbovePelvis = 0.50f;
        public float ankleHeightAboveDeck = 0.06f;
        public Vector3 frontFoot = new Vector3(-0.02f, 0f, 0.04f); // y игнорируется
        public Vector3 rearFoot = new Vector3(0.02f, 0f, -0.15f);

        [Header("Приводы суставов")]
        public DriveSettings spine = new DriveSettings(3000f, 300f, 4000f);
        public DriveSettings shoulder = new DriveSettings(600f, 60f, 1500f);
        public DriveSettings elbow = new DriveSettings(400f, 40f, 1000f);
        public DriveSettings hip = new DriveSettings(3000f, 300f, 4000f);
        public DriveSettings knee = new DriveSettings(3000f, 300f, 4000f);
        public DriveSettings ankle = new DriveSettings(300f, 30f, 600f);

        [Tooltip("Лимит каждой оси сферических суставов от позы сборки, градусы. 0 — без лимитов. " +
                 "Временная заглушка: равномерные ±60° мешают приземлению (спайк S1), нужны анатомические диапазоны (этап 3).")]
        public float sphericalLimit;

        public float footFriction = 1.0f;
    }

    public sealed class RiderParts
    {
        public ArticulationBody Pelvis, Torso;
        public ArticulationBody UpperArmL, UpperArmR, ForearmL, ForearmR;
        public ArticulationBody ThighL, ThighR, ShinL, ShinR, FootL, FootR;
        public ArticulationBody[] All;
        public Vector3 HandLocalL, HandLocalR;  // точка хвата в локальных координатах предплечья
        public Vector3 SoleLocalL, SoleLocalR;  // центр подошвы в локальных координатах стопы
        public bool ArmsReachable, LegsReachable;
    }

    /// Упрощённое тело райдера на ArticulationBody: 13 звеньев, без кистей, шеи и позвоночника.
    /// Левая нога — передняя (regular stance), райдер смотрит вдоль +Z рига.
    public static class RiderRigBuilder
    {
        public static RiderParts Build(Transform container, Pose pose, RiderBuildSettings s, int layer,
            float deckTopY, Vector3 gripL, Vector3 gripR)
        {
            var p = new RiderParts();
            var all = new List<ArticulationBody>();
            Vector3 W(Vector3 local) => pose.position + pose.rotation * local;
            Vector3 Dir(Vector3 local) => pose.rotation * local;

            // Ключевые точки в координатах рига
            Vector3 pelvisC = s.pelvis;
            Vector3 lumbar = pelvisC + new Vector3(0f, 0.08f, 0f);
            Vector3 neck = pelvisC + new Vector3(0f, s.shoulderHeightAbovePelvis + 0.07f, 0.02f);
            Vector3 shoulderL = pelvisC + new Vector3(-s.shoulderHalfWidth, s.shoulderHeightAbovePelvis, 0.02f);
            Vector3 shoulderR = pelvisC + new Vector3(s.shoulderHalfWidth, s.shoulderHeightAbovePelvis, 0.02f);
            Vector3 hipL = pelvisC + new Vector3(-s.hipHalfWidth, -0.04f, 0f);
            Vector3 hipR = pelvisC + new Vector3(s.hipHalfWidth, -0.04f, 0f);
            float ankleY = deckTopY + s.ankleHeightAboveDeck;
            Vector3 ankleL = new Vector3(s.frontFoot.x, ankleY, s.frontFoot.z);
            Vector3 ankleR = new Vector3(s.rearFoot.x, ankleY, s.rearFoot.z);

            Vector3 elbowL = TwoBoneIK.SolveMidJoint(shoulderL, gripL, s.upperArmLength, s.forearmLength, new Vector3(-1f, 0f, -0.5f), out bool armL);
            Vector3 elbowR = TwoBoneIK.SolveMidJoint(shoulderR, gripR, s.upperArmLength, s.forearmLength, new Vector3(1f, 0f, -0.5f), out bool armR);
            Vector3 kneeL = TwoBoneIK.SolveMidJoint(hipL, ankleL, s.thighLength, s.shinLength, Vector3.forward, out bool legL);
            Vector3 kneeR = TwoBoneIK.SolveMidJoint(hipR, ankleR, s.thighLength, s.shinLength, Vector3.forward, out bool legR);
            p.ArmsReachable = armL && armR;
            p.LegsReachable = legL && legR;

            var footMat = ScooterRigBuilder.Material("Foot", s.footFriction);

            // Таз — корень articulation
            var pelvisGo = new GameObject("Pelvis") { layer = layer };
            pelvisGo.transform.SetParent(container, false);
            pelvisGo.transform.SetPositionAndRotation(W(pelvisC), pose.rotation);
            AddBox(pelvisGo, Vector3.zero, new Vector3(0.30f, 0.16f, 0.20f), null);
            p.Pelvis = pelvisGo.AddComponent<ArticulationBody>();
            p.Pelvis.mass = s.pelvisMass;
            Common(p.Pelvis);
            all.Add(p.Pelvis);

            // Корпус (голова и плечевой пояс в массе корпуса)
            p.Torso = Segment("Torso", p.Pelvis.transform, layer, W(lumbar), W(neck), Dir(Vector3.forward), 0.14f, s.torsoMass, null);
            Spherical(p.Torso, Dir(Vector3.up), Dir(Vector3.forward), s.spine);
            all.Add(p.Torso);

            // Руки
            p.UpperArmL = Segment("UpperArmL", p.Torso.transform, layer, W(shoulderL), W(elbowL), Dir(Vector3.up), 0.045f, s.upperArmMass, null);
            Spherical(p.UpperArmL, Dir(elbowL - shoulderL), Dir(Vector3.up), s.shoulder);
            p.UpperArmR = Segment("UpperArmR", p.Torso.transform, layer, W(shoulderR), W(elbowR), Dir(Vector3.up), 0.045f, s.upperArmMass, null);
            Spherical(p.UpperArmR, Dir(elbowR - shoulderR), Dir(Vector3.up), s.shoulder);
            p.ForearmL = Segment("ForearmL", p.UpperArmL.transform, layer, W(elbowL), W(gripL), Dir(Vector3.up), 0.04f, s.forearmMass, null);
            Revolute(p.ForearmL, Dir(HingeAxis(shoulderL, elbowL, gripL, Vector3.right)), s.elbow);
            p.ForearmR = Segment("ForearmR", p.UpperArmR.transform, layer, W(elbowR), W(gripR), Dir(Vector3.up), 0.04f, s.forearmMass, null);
            Revolute(p.ForearmR, Dir(HingeAxis(shoulderR, elbowR, gripR, Vector3.right)), s.elbow);
            p.HandLocalL = p.ForearmL.transform.InverseTransformPoint(W(gripL));
            p.HandLocalR = p.ForearmR.transform.InverseTransformPoint(W(gripR));
            all.Add(p.UpperArmL); all.Add(p.UpperArmR); all.Add(p.ForearmL); all.Add(p.ForearmR);

            // Ноги
            p.ThighL = Segment("ThighL", p.Pelvis.transform, layer, W(hipL), W(kneeL), Dir(Vector3.forward), 0.065f, s.thighMass, null);
            Spherical(p.ThighL, Dir(kneeL - hipL), Dir(Vector3.forward), s.hip);
            p.ThighR = Segment("ThighR", p.Pelvis.transform, layer, W(hipR), W(kneeR), Dir(Vector3.forward), 0.065f, s.thighMass, null);
            Spherical(p.ThighR, Dir(kneeR - hipR), Dir(Vector3.forward), s.hip);
            p.ShinL = Segment("ShinL", p.ThighL.transform, layer, W(kneeL), W(ankleL), Dir(Vector3.forward), 0.05f, s.shinMass, null);
            Revolute(p.ShinL, Dir(HingeAxis(hipL, kneeL, ankleL, Vector3.right)), s.knee);
            p.ShinR = Segment("ShinR", p.ThighR.transform, layer, W(kneeR), W(ankleR), Dir(Vector3.forward), 0.05f, s.shinMass, null);
            Revolute(p.ShinR, Dir(HingeAxis(hipR, kneeR, ankleR, Vector3.right)), s.knee);
            all.Add(p.ThighL); all.Add(p.ThighR); all.Add(p.ShinL); all.Add(p.ShinR);

            // Стопы: выровнены по ригу, подошва на верхе деки
            p.FootL = Foot("FootL", p.ShinL.transform, layer, W(ankleL), pose.rotation, s, footMat, out p.SoleLocalL);
            p.FootR = Foot("FootR", p.ShinR.transform, layer, W(ankleR), pose.rotation, s, footMat, out p.SoleLocalR);
            all.Add(p.FootL); all.Add(p.FootR);

            p.All = all.ToArray();
            if (s.sphericalLimit > 0f) LimitSphericals(p.All, s.sphericalLimit);
            return p;
        }

        /// Временная заглушка анатомических лимитов: одинаковый диапазон по всем осям сферических суставов.
        static void LimitSphericals(ArticulationBody[] links, float limit)
        {
            foreach (var ab in links)
            {
                if (ab.isRoot || ab.jointType != ArticulationJointType.SphericalJoint) continue;
                ab.twistLock = ArticulationDofLock.LimitedMotion;
                ab.swingYLock = ArticulationDofLock.LimitedMotion;
                ab.swingZLock = ArticulationDofLock.LimitedMotion;
                ab.xDrive = WithLimits(ab.xDrive, limit);
                ab.yDrive = WithLimits(ab.yDrive, limit);
                ab.zDrive = WithLimits(ab.zDrive, limit);
            }
        }

        static ArticulationDrive WithLimits(ArticulationDrive d, float limit)
        {
            d.lowerLimit = -limit;
            d.upperLimit = limit;
            return d;
        }

        static ArticulationBody Foot(string name, Transform parent, int layer, Vector3 ankleWorld, Quaternion rotation,
            RiderBuildSettings s, PhysicsMaterial mat, out Vector3 soleLocal)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(ankleWorld, rotation);
            float h = s.ankleHeightAboveDeck;
            // Стопа 22 см: 7 см назад от голеностопа, 15 см вперёд.
            var center = new Vector3(0f, -h * 0.5f - 0.005f, 0.04f);
            AddBox(go, center, new Vector3(0.09f, h - 0.01f, 0.22f), mat);
            soleLocal = new Vector3(0f, -h, 0.04f);
            var ab = go.AddComponent<ArticulationBody>();
            ab.mass = s.footMass;
            Common(ab);
            Spherical(ab, rotation * Vector3.up, rotation * Vector3.forward, s.ankle);
            return ab;
        }

        /// Звено вдоль отрезка: локальная +Z направлена от сустава к концу, капсула по Z.
        static ArticulationBody Segment(string name, Transform parent, int layer, Vector3 jointWorld, Vector3 endWorld,
            Vector3 upHint, float radius, float mass, PhysicsMaterial mat)
        {
            Vector3 dir = endWorld - jointWorld;
            float length = dir.magnitude;
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(jointWorld, Quaternion.LookRotation(dir / length, upHint));
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 2;
            c.radius = radius;
            c.height = length;
            c.center = new Vector3(0f, 0f, length * 0.5f);
            c.sharedMaterial = mat;
            var ab = go.AddComponent<ArticulationBody>();
            ab.mass = mass;
            Common(ab);
            return ab;
        }

        static void Common(ArticulationBody ab)
        {
            ab.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            ab.linearDamping = 0.02f;
            ab.angularDamping = 0.05f;
            ab.jointFriction = 0.02f;
            ab.sleepThreshold = 0f;
        }

        /// Ось шарнира перпендикулярна плоскости цепочки.
        static Vector3 HingeAxis(Vector3 a, Vector3 mid, Vector3 b, Vector3 fallback)
        {
            Vector3 axis = Vector3.Cross(mid - a, b - mid);
            return axis.sqrMagnitude > 1e-8f ? axis.normalized : fallback;
        }

        static void Spherical(ArticulationBody ab, Vector3 twistAxisWorld, Vector3 secondaryWorld, DriveSettings d)
        {
            ab.jointType = ArticulationJointType.SphericalJoint;
            ab.twistLock = ArticulationDofLock.FreeMotion;
            ab.swingYLock = ArticulationDofLock.FreeMotion;
            ab.swingZLock = ArticulationDofLock.FreeMotion;
            SetFrame(ab, twistAxisWorld, secondaryWorld);
            ab.xDrive = Drive(d);
            ab.yDrive = Drive(d);
            ab.zDrive = Drive(d);
        }

        static void Revolute(ArticulationBody ab, Vector3 axisWorld, DriveSettings d)
        {
            ab.jointType = ArticulationJointType.RevoluteJoint;
            ab.twistLock = ArticulationDofLock.FreeMotion;
            Vector3 secondary = Mathf.Abs(Vector3.Dot(axisWorld, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward;
            SetFrame(ab, axisWorld, secondary);
            ab.xDrive = Drive(d);
        }

        /// Ось X якоря сустава = primaryWorld. Якорь в начале звена, родительский якорь подбирается автоматически.
        static void SetFrame(ArticulationBody ab, Vector3 primaryWorld, Vector3 secondaryWorld)
        {
            Vector3 x = primaryWorld.normalized;
            Vector3 z = Vector3.Cross(x, secondaryWorld);
            if (z.sqrMagnitude < 1e-8f) z = Vector3.Cross(x, Vector3.right);
            z.Normalize();
            Vector3 y = Vector3.Cross(z, x);
            Quaternion frameWorld = Quaternion.LookRotation(z, y);
            ab.matchAnchors = true;
            ab.anchorPosition = Vector3.zero;
            ab.anchorRotation = Quaternion.Inverse(ab.transform.rotation) * frameWorld;
        }

        static ArticulationDrive Drive(DriveSettings d) => new ArticulationDrive
        {
            driveType = d.type,
            stiffness = d.stiffness,
            damping = d.damping,
            forceLimit = d.forceLimit,
            target = 0f,
            targetVelocity = 0f,
            lowerLimit = -180f,
            upperLimit = 180f,
        };

        static void AddBox(GameObject go, Vector3 center, Vector3 size, PhysicsMaterial mat)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            c.sharedMaterial = mat;
        }
    }
}
