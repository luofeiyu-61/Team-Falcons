using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    private const string DefaultGateSoundResourcePath = "Audio/in";

    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer;

    [Header("检查点ID")]
    [SerializeField] private string checkpointId = "Exit";

    [Header("触发音效")]
    [Tooltip("MainCharacter 进入该 Gate/Checkpoint 时播放的音频，可在 Inspector 中自行选择")]
    [SerializeField] private AudioClip triggerSound;

    [Range(0f, 1f)]
    [SerializeField] private float triggerSoundVolume = 1f;

    private bool hasTriggered;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTriggered || (playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        hasTriggered = true;
        PlayTriggerSound();
        GameEventBus.Publish(new ExitReachedEvent(checkpointId));
    }

    private void PlayTriggerSound()
    {
        AudioClip clipToPlay = triggerSound != null
            ? triggerSound
            : Resources.Load<AudioClip>(DefaultGateSoundResourcePath);

        if (clipToPlay == null)
        {
            Debug.LogWarning($"{nameof(Checkpoint)} 没有设置触发音效，也没有在 Resources/{DefaultGateSoundResourcePath} 找到默认音效。", this);
            return;
        }

        GameObject audioObject = new GameObject("Gate Trigger Sound");
        audioObject.transform.position = transform.position;
        DontDestroyOnLoad(audioObject);

        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.clip = clipToPlay;
        audioSource.volume = triggerSoundVolume;
        audioSource.spatialBlend = 0f;

        if (FindObjectOfType<AudioListener>() == null)
        {
            audioObject.AddComponent<AudioListener>();
        }

        audioSource.Play();
        Destroy(audioObject, clipToPlay.length);
    }
}
