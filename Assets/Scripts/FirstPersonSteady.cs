using UnityEngine;

// Makes first person feel calm instead of disorienting:
// - The upper body and arms stop swinging and bobbing with the walk/run animation
// - The arms and gun bend up and down with where you look
// - The camera's head bob is smoothed out
// Put this on the PLAYER (the object with the Animator, or its parent).
[DefaultExecutionOrder(50)]   // after the animation and PlayerMovement, before MouseLook
public class FirstPersonSteady : MonoBehaviour
{
    [Tooltip("How still the chest, shoulders and neck stay while walking. 0 = normal animation, 1 = very still.")]
    [Range(0f, 1f)] public float bodySteadiness = 0.8f;
    [Tooltip("How much the arms ignore the walk swing. Too high makes reloading look slow.")]
    [Range(0f, 1f)] public float armSteadiness = 0.5f;
    [Tooltip("How much the arms and gun tilt up and down with where you look.")]
    [Range(0f, 1f)] public float followLook = 0.7f;
    [Tooltip("How much of the camera's up-and-down bob is removed.")]
    [Range(0f, 1f)] public float cameraSteadiness = 0.85f;

    Animator anim;
    Transform hips;
    float hipsY;
    bool hipsReady;

    Transform[] body, arms, spine;
    Quaternion[] bodySmooth, armSmooth;
    bool ready;

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman)
        {
            Debug.LogWarning("FirstPersonSteady: needs a Humanoid Animator on the player.");
            enabled = false;
            return;
        }

        hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        body = Bones(HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck,
                     HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder);
        arms = Bones(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
                     HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                     HumanBodyBones.LeftHand, HumanBodyBones.RightHand);
        spine = Bones(HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest);
        bodySmooth = new Quaternion[body.Length];
        armSmooth = new Quaternion[arms.Length];

        // Smooth the camera's bob too
        Camera cam = Camera.main;
        if (cam != null)
        {
            CameraSteady cs = cam.GetComponent<CameraSteady>();
            if (cs == null) cs = cam.gameObject.AddComponent<CameraSteady>();
            cs.owner = this;
            cs.head = anim.GetBoneTransform(HumanBodyBones.Head);
        }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // 1. Less bouncing: the hips' up-and-down bob is smoothed
        if (hips != null)
        {
            Vector3 local = transform.InverseTransformPoint(hips.position);
            if (!hipsReady) { hipsY = local.y; hipsReady = true; }
            hipsY = Mathf.Lerp(hipsY, local.y, Follow(bodySteadiness, dt));
            local.y = hipsY;
            hips.position = transform.TransformPoint(local);
        }

        // 2. Chest, neck and shoulders stay calm; arms swing less
        if (!ready)
        {
            for (int i = 0; i < body.Length; i++) if (body[i] != null) bodySmooth[i] = body[i].localRotation;
            for (int i = 0; i < arms.Length; i++) if (arms[i] != null) armSmooth[i] = arms[i].localRotation;
            ready = true;
        }
        Smooth(body, bodySmooth, Follow(bodySteadiness, dt));
        Smooth(arms, armSmooth, Follow(armSteadiness, dt));

        // 3. Bend the upper body with the camera's up/down look, so the arms and gun point where you look
        Camera cam = Camera.main;
        if (cam != null && followLook > 0f)
        {
            float pitch = cam.transform.eulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
            int count = 0;
            foreach (Transform t in spine) if (t != null) count++;
            if (count > 0)
            {
                float share = pitch * followLook / count;
                foreach (Transform t in spine)
                    if (t != null) t.rotation = Quaternion.AngleAxis(share, transform.right) * t.rotation;
            }
        }
    }

    void Smooth(Transform[] bones, Quaternion[] smooth, float follow)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            smooth[i] = Quaternion.Slerp(smooth[i], bones[i].localRotation, follow);
            bones[i].localRotation = smooth[i];
        }
    }

    // 0 steadiness = follow instantly, 1 = follow very slowly
    float Follow(float steadiness, float dt)
    {
        return 1f - Mathf.Exp(-Mathf.Lerp(40f, 2f, steadiness) * dt);
    }

    Transform[] Bones(params HumanBodyBones[] ids)
    {
        Transform[] result = new Transform[ids.Length];
        for (int i = 0; i < ids.Length; i++) result[i] = anim.GetBoneTransform(ids[i]);
        return result;
    }
}

// Added to the camera automatically by FirstPersonSteady. Runs after MouseLook and smooths the head bob.
[DefaultExecutionOrder(200)]
public class CameraSteady : MonoBehaviour
{
    [HideInInspector] public FirstPersonSteady owner;
    [HideInInspector] public Transform head;
    Vector3 smoothLocal;
    bool ready;

    void LateUpdate()
    {
        if (owner == null || !owner.enabled) return;
        Transform body = owner.transform;

        // Third person (camera far from the head): leave it alone
        if (head != null && Vector3.Distance(transform.position, head.position) > 1f) { ready = false; return; }

        Vector3 local = body.InverseTransformPoint(transform.position);
        if (!ready) { smoothLocal = local; ready = true; }

        float dt = Time.deltaTime;
        float steady = owner.cameraSteadiness;
        float upDown = 1f - Mathf.Exp(-Mathf.Lerp(40f, 3f, steady) * dt);
        float sides = 1f - Mathf.Exp(-Mathf.Lerp(40f, 10f, steady) * dt);

        smoothLocal.x = Mathf.Lerp(smoothLocal.x, local.x, sides);
        smoothLocal.y = Mathf.Lerp(smoothLocal.y, local.y, upDown);
        smoothLocal.z = Mathf.Lerp(smoothLocal.z, local.z, sides);
        transform.position = body.TransformPoint(smoothLocal);
    }
}