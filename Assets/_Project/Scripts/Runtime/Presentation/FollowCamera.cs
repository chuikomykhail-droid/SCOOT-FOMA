using UnityEngine;

namespace ScootFoma.Presentation
{
    /// Простая камера-преследователь. Следит за визуалом (уже интерполированным), а не за физикой.
    [DefaultExecutionOrder(1000)]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] Vector3 offset = new Vector3(2.5f, 1.6f, -3.5f);
        [SerializeField, Min(0f)] float positionSharpness = 6f;
        [SerializeField] bool followTargetYaw;

        public Transform Target { get => target; set => target = value; }

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 worldOffset = offset;
            if (followTargetYaw)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up);
                if (fwd.sqrMagnitude > 1e-4f) worldOffset = Quaternion.LookRotation(fwd) * offset;
            }
            Vector3 desired = target.position + worldOffset;
            float k = 1f - Mathf.Exp(-positionSharpness * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 0.6f - transform.position);
        }
    }
}
