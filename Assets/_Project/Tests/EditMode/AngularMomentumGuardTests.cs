using NUnit.Framework;
using ScootFoma.Sensing;
using UnityEngine;

namespace ScootFoma.Tests
{
    public class AngularMomentumGuardTests
    {
        static BodyMassState[] TwoBodies(Vector3 omega)
        {
            // Две «гантели» на расстоянии 1 м, вращаются как одно твёрдое тело.
            var a = new BodyMassState
            {
                Mass = 2f, CenterOfMass = new Vector3(-0.5f, 0f, 0f),
                InertiaDiagonal = new Vector3(0.1f, 0.2f, 0.3f), InertiaRotation = Quaternion.Euler(10f, 20f, 30f),
            };
            var b = new BodyMassState
            {
                Mass = 3f, CenterOfMass = new Vector3(0.7f, 0.2f, 0.1f),
                InertiaDiagonal = new Vector3(0.05f, 0.4f, 0.1f), InertiaRotation = Quaternion.Euler(-40f, 5f, 0f),
            };
            var bodies = new[] { a, b };
            Vector3 com = MomentumMath.CenterOfMass(bodies, 2, out _);
            for (int i = 0; i < 2; i++)
            {
                bodies[i].AngularVelocity = omega;
                bodies[i].Velocity = Vector3.Cross(omega, bodies[i].CenterOfMass - com);
            }
            return bodies;
        }

        static void Apply(BodyMassState[] bodies, Vector3 deltaOmega, Vector3 com)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].AngularVelocity += deltaOmega;
                bodies[i].Velocity += Vector3.Cross(deltaOmega, bodies[i].CenterOfMass - com);
            }
        }

        [Test]
        public void NoError_NoCorrection()
        {
            var bodies = TwoBodies(new Vector3(1f, 5f, 2f));
            Vector3 com = MomentumMath.CenterOfMass(bodies, 2, out _);
            Vector3 l = MomentumMath.TotalAngularMomentum(bodies, 2, com);
            Vector3 d = AngularMomentumGuard.Correction(bodies, 2, l, 1f, out _);
            Assert.Less(d.magnitude, 1e-4f);
        }

        [Test]
        public void Correction_RestoresTargetMomentum()
        {
            var reference = TwoBodies(new Vector3(1f, 5f, 2f));
            Vector3 com = MomentumMath.CenterOfMass(reference, 2, out _);
            Vector3 target = MomentumMath.TotalAngularMomentum(reference, 2, com);

            var drifted = TwoBodies(new Vector3(1.4f, 6.5f, 1.2f)); // «утечка» изменила вращение
            Vector3 d = AngularMomentumGuard.Correction(drifted, 2, target, 10f, out Vector3 c);
            Apply(drifted, d, c);

            Vector3 after = MomentumMath.TotalAngularMomentum(drifted, 2, c);
            Assert.Less((after - target).magnitude / target.magnitude, 1e-3f, $"target={target} after={after}");
        }

        [Test]
        public void Correction_IsClampedPerStep()
        {
            var reference = TwoBodies(new Vector3(0f, 5f, 0f));
            Vector3 com = MomentumMath.CenterOfMass(reference, 2, out _);
            Vector3 target = MomentumMath.TotalAngularMomentum(reference, 2, com);
            var drifted = TwoBodies(new Vector3(0f, 10f, 0f));

            Vector3 d = AngularMomentumGuard.Correction(drifted, 2, target, 0.01f, out Vector3 c);
            Apply(drifted, d, c);
            Vector3 before = MomentumMath.TotalAngularMomentum(TwoBodies(new Vector3(0f, 10f, 0f)), 2, c);
            Vector3 after = MomentumMath.TotalAngularMomentum(drifted, 2, c);
            Assert.AreEqual(0.01f * target.magnitude, (before - after).magnitude, 1e-3f);
        }
    }
}
