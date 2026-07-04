using UnityEngine;

public class MonoSingletonScript : MonoBehaviour
{
    public static MonoSingletonScript Instance;

    protected virtual void SafeAwake()
    {
        
    }

    protected void Awake()
    {
        if (Instance == null)
        {
            Instance = gameObject.GetComponent<MonoSingletonScript>();
            SafeAwake();
        }
        else
        {
            Destroy(gameObject);
        }
    }
}