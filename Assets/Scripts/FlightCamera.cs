using UnityEngine;

public class FlightCamera : MonoBehaviour
{
    [Header("Chase Camera")]
    [SerializeField] Vector3 chaseOffset = new Vector3(0f, 3f, -12f);
    [SerializeField] float chaseSmooth = 6f;

    [Header("Cockpit Camera")]
    [SerializeField] Vector3 cockpitOffset = new Vector3(0f, 0.26f, 0.80f);

    [Header("Toggle")]
    [SerializeField] KeyCode toggleKey = KeyCode.C;

    bool isCockpit = false;
    PlayerController localPlayer;
    Renderer[] localRenderers;

    void Start()
    {
        FindLocalPlayer();
    }

    void FindLocalPlayer()
    {
        foreach (PlayerController pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.isLocalPlayer)
            {
                localPlayer = pc;
                localRenderers = pc.GetComponentsInChildren<Renderer>();
                break;
            }
        }
    }

    void LateUpdate()
    {
        if (localPlayer == null)
        {
            FindLocalPlayer();
            return;
        }

        if (Input.GetKeyDown(toggleKey))
            SetCockpit(!isCockpit);

        if (isCockpit)
            UpdateCockpit();
        else
            UpdateChase();
    }

    void UpdateCockpit()
    {
        Transform t = localPlayer.transform;
        transform.position = t.TransformPoint(cockpitOffset);
        transform.rotation = t.rotation;
    }

    void UpdateChase()
    {
        Transform t = localPlayer.transform;
        Vector3 target = t.TransformPoint(chaseOffset);
        transform.position = Vector3.Lerp(transform.position, target, chaseSmooth * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, t.rotation, chaseSmooth * Time.deltaTime);
    }

    void SetCockpit(bool cockpit)
    {
        isCockpit = cockpit;
        if (localRenderers == null) return;
        foreach (Renderer r in localRenderers)
            r.enabled = !cockpit;
    }
}
