using NUnit.Framework;
using ScootFoma.Control;
using ScootFoma.Grip;
using ScootFoma.Sensing;
using UnityEngine;

namespace ScootFoma.Tests
{
    public class TwoBoneIKTests
    {
        [Test]
        public void MidJoint_KeepsBoneLengths()
        {
            var root = new Vector3(0f, 1f, 0f);
            var target = new Vector3(0.2f, 0.4f, 0.3f);
            Vector3 mid = TwoBoneIK.SolveMidJoint(root, target, 0.42f, 0.42f, Vector3.forward, out bool reachable);
            Assert.IsTrue(reachable);
            Assert.AreEqual(0.42f, Vector3.Distance(root, mid), 1e-4f);
            Assert.AreEqual(0.42f, Vector3.Distance(mid, target), 1e-4f);
        }

        [Test]
        public void MidJoint_BendsTowardHint()
        {
            Vector3 mid = TwoBoneIK.SolveMidJoint(Vector3.up, Vector3.zero, 0.6f, 0.6f, Vector3.forward, out _);
            Assert.Greater(mid.z, 0f);
        }

        [Test]
        public void Unreachable_Reported()
        {
            TwoBoneIK.SolveMidJoint(Vector3.zero, Vector3.up * 2f, 0.5f, 0.5f, Vector3.forward, out bool reachable);
            Assert.IsFalse(reachable);
        }
    }

    public class CappedSpringTests
    {
        [Test]
        public void Force_IsClampedToMax()
        {
            Vector3 f = CappedSpring.Force(Vector3.up, Vector3.zero, 10000f, 0f, 300f);
            Assert.AreEqual(300f, f.magnitude, 1e-3f);
        }

        [Test]
        public void ZeroMax_GivesNoForce()
        {
            Assert.AreEqual(Vector3.zero, CappedSpring.Force(Vector3.up, Vector3.up, 1000f, 10f, 0f));
        }
    }

    public class MomentumMathTests
    {
        [Test]
        public void SpinningBody_MomentumIsInertiaTimesOmega()
        {
            var body = new BodyMassState
            {
                Mass = 2f,
                CenterOfMass = Vector3.zero,
                AngularVelocity = new Vector3(0f, 3f, 0f),
                InertiaDiagonal = new Vector3(1f, 0.5f, 1f),
                InertiaRotation = Quaternion.identity,
            };
            Vector3 l = MomentumMath.AngularMomentum(body, Vector3.zero);
            Assert.AreEqual(1.5f, l.y, 1e-5f);
        }

        [Test]
        public void OrbitalPart_IsMassRCrossV()
        {
            var body = new BodyMassState
            {
                Mass = 2f,
                CenterOfMass = new Vector3(1f, 0f, 0f),
                Velocity = new Vector3(0f, 0f, 1f),
                InertiaRotation = Quaternion.identity,
            };
            Vector3 l = MomentumMath.AngularMomentum(body, Vector3.zero);
            Assert.AreEqual(-2f, l.y, 1e-5f);
        }
    }
}
