using UnityEngine;

namespace Sandbox.Debugging
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConeDetection2D))]
    public sealed class ConeConsoleLogger : MonoBehaviour
    {
        [SerializeField] private string enterMessage = "Player entered cone";
        [SerializeField] private string exitMessage = "Player exited cone";
        [SerializeField] private bool includeObjectName = true;

        private ConeDetection2D detection;

        private void Awake()
        {
            detection = GetComponent<ConeDetection2D>();
        }

        

        

        public void LogEnter()
        {
            string msg = includeObjectName ? $"[{name}] {enterMessage}" : enterMessage;
            Debug.Log(msg, this);
        }

        public void LogExit()
        {
            string msg = includeObjectName ? $"[{name}] {exitMessage}" : exitMessage;
            Debug.Log(msg, this);
        }
    }
}
