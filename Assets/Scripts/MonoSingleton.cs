using UnityEngine;

public abstract class MonoSingleton<T> : MonoBehaviour
{
    public static T Instance;

    protected virtual void SafeAwake()
    {
        
    }
    
    protected void Awake()
    {
        if (Instance == null)
        {
            Instance = gameObject.GetComponent<T>();
            SafeAwake();
        }
        else
            Destroy(gameObject);
    }
}
