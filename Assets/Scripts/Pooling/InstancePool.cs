using System.Collections.Generic;
using UnityEngine;

namespace Sandbox.Pooling
{
    [DisallowMultipleComponent]
    public sealed class InstancePool : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private GameObject prefab;

        [Header("Behaviour")]
        [SerializeField] [Min(0)] private int initialSize = 4;
        [SerializeField] private bool warmOnAwake = true;
        [SerializeField] private bool autoExpand = true;
        [SerializeField] private bool verboseWarnings = true;
        [SerializeField] private Transform instancesParent;

        private readonly Queue<GameObject> available = new Queue<GameObject>();
        private readonly HashSet<GameObject> inUse = new HashSet<GameObject>();
        private readonly List<GameObject> returnBuffer = new List<GameObject>();
        private readonly Dictionary<GameObject, IPooledInstance> listeners = new Dictionary<GameObject, IPooledInstance>();

        private bool initialized;

        private void Awake()
        {
            if (warmOnAwake)
            {
                EnsureInitialized();
            }
        }

        public void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            if (prefab == null)
            {
                Debug.LogError($"InstancePool on {name} requires a prefab reference before it can be used.", this);
                return;
            }

            if (instancesParent == null)
            {
                instancesParent = transform;
            }

            Prewarm(initialSize);
            initialized = true;
        }

        public void Prewarm(int targetCount)
        {
            if (prefab == null)
            {
                return;
            }

            if (instancesParent == null)
            {
                instancesParent = transform;
            }

            int totalCount = available.Count + inUse.Count;
            while (totalCount < targetCount)
            {
                available.Enqueue(CreateInstance());
                totalCount++;
            }
        }

        public GameObject Get()
        {
            var instance = PrepareInstance(instancesParent, false);
            if (instance == null)
            {
                return null;
            }

            var instanceTransform = instance.transform;
            instanceTransform.localPosition = Vector3.zero;
            instanceTransform.localRotation = Quaternion.identity;

            ActivateInstance(instance);
            return instance;
        }

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            var instance = PrepareInstance(instancesParent, true);
            if (instance == null)
            {
                return null;
            }

            instance.transform.SetPositionAndRotation(position, rotation);
            ActivateInstance(instance);
            return instance;
        }

        public GameObject Get(Transform parent, bool worldPositionStays = false)
        {
            var instance = PrepareInstance(parent, worldPositionStays);
            if (instance == null)
            {
                return null;
            }

            if (!worldPositionStays)
            {
                var instanceTransform = instance.transform;
                instanceTransform.localPosition = Vector3.zero;
                instanceTransform.localRotation = Quaternion.identity;
            }

            ActivateInstance(instance);
            return instance;
        }

        public GameObject Get(Vector3 position, Quaternion rotation, Transform parent, bool worldPositionStays = true)
        {
            var instance = PrepareInstance(parent, worldPositionStays);
            if (instance == null)
            {
                return null;
            }

            var instanceTransform = instance.transform;
            if (worldPositionStays)
            {
                instanceTransform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                instanceTransform.localPosition = position;
                instanceTransform.localRotation = rotation;
            }

            ActivateInstance(instance);
            return instance;
        }

        public void Return(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (!inUse.Remove(instance))
            {
                if (verboseWarnings)
                {
                    Debug.LogWarning($"Instance {instance.name} does not belong to pool {name}.", this);
                }

                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(instancesParent, false);

            if (listeners.TryGetValue(instance, out var listener))
            {
                listener.OnReturnedToPool();
            }

            available.Enqueue(instance);
        }

        public void ReturnAll()
        {
            if (inUse.Count == 0)
            {
                return;
            }

            returnBuffer.Clear();
            returnBuffer.AddRange(inUse);

            foreach (var instance in returnBuffer)
            {
                Return(instance);
            }

            returnBuffer.Clear();
        }

        public bool Contains(GameObject instance)
        {
            return instance != null && (inUse.Contains(instance) || available.Contains(instance));
        }

        private GameObject TakeInstance()
        {
            EnsureInitialized();

            if (prefab == null)
            {
                return null;
            }

            if (available.Count == 0)
            {
                if (autoExpand)
                {
                    available.Enqueue(CreateInstance());
                }
                else
                {
                    if (verboseWarnings)
                    {
                        Debug.LogWarning($"InstancePool on {name} is empty. Increase the initial size or enable auto expand.", this);
                    }

                    return null;
                }
            }

            var instance = available.Dequeue();
            inUse.Add(instance);
            return instance;
        }

        private void ActivateInstance(GameObject instance)
        {
            instance.SetActive(true);

            if (listeners.TryGetValue(instance, out var listener))
            {
                listener.OnTakenFromPool();
            }
        }

        private GameObject PrepareInstance(Transform parent, bool worldPositionStays)
        {
            var instance = TakeInstance();
            if (instance == null)
            {
                return null;
            }

            var targetParent = parent != null ? parent : instancesParent;
            instance.transform.SetParent(targetParent, worldPositionStays);
            return instance;
        }

        private GameObject CreateInstance()
        {
            var instance = Instantiate(prefab, instancesParent);
            instance.SetActive(false);

            if (instance.TryGetComponent(out IPooledInstance pooled))
            {
                pooled.OnCreatedForPool(this);
                listeners[instance] = pooled;
            }

            return instance;
        }

        public interface IPooledInstance
        {
            void OnCreatedForPool(InstancePool pool);
            void OnTakenFromPool();
            void OnReturnedToPool();
        }
    }
}
