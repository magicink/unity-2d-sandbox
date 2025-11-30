using UnityEngine;

public class PhysicsGameCameraController : MonoBehaviour
{
    [Header("References")] 
    [Tooltip("Assign the PhysicsGamePlayer instance this camera should follow or otherwise reference in the inspector.")]
    [SerializeField] private PhysicsGamePlayer player = null;
    [Header("Follow")]
    [Tooltip("How quickly the camera interpolates horizontally toward the player's X position. Larger values = faster following; set to 0 for instant.")]
    [SerializeField] private float horizontalLerpSpeed = 10f;
    [Tooltip("If true, the camera will instantly snap to the player at start (useful for set up).")]
    [SerializeField] private bool snapOnStart = true;
    private bool hasInitialSnap;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        hasInitialSnap = false;
        
    }

    // LateUpdate is called once per frame after all Update steps -- better for camera movement
    void LateUpdate()
    {
        // If we have no player, there is nothing to follow
        if (player == null) return;

        // Get the current and target positions but only consider the X coordinate for following
        Vector3 currentPos = transform.position;
        Vector3 targetPos = transform.position; // default
        if (player != null)
        {
            targetPos = new Vector3(player.transform.position.x, currentPos.y, currentPos.z);
        }

        // Snap immediately if requested or if speed == 0
        if (snapOnStart && !hasInitialSnap)
        {
            transform.position = targetPos;
            hasInitialSnap = true;
            return;
        }

        if (horizontalLerpSpeed <= 0f)
        {
            transform.position = targetPos;
            return;
        }

        // Lerp the X coordinate; use deltaTime-scaled interpolation
        float t = Mathf.Clamp01(horizontalLerpSpeed * Time.deltaTime);
        float newX = Mathf.Lerp(currentPos.x, targetPos.x, t);
        transform.position = new Vector3(newX, currentPos.y, currentPos.z);
    }
}
