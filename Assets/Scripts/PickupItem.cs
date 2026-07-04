using UnityEngine;

public class PickupItem : MonoBehaviour
{
    private const string DefaultPickupSoundResourcePath = "Audio/PickUp";

    [Header("道具类型")]
    [SerializeField] private AnchorMode pickupMode = AnchorMode.Attract;

    [Header("增加额度")]
    [SerializeField] private int chargeAmount = 1;

    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer;

    [Header("拾取音效")]
    [Tooltip("MainCharacter 拾取该道具时播放的音频，可在 Inspector 中自行选择")]
    [SerializeField] private AudioClip pickupSound;

    [Range(0f, 1f)]
    [SerializeField] private float pickupSoundVolume = 1f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        GameEventBus.Publish(
            new AnchorModeChangedEvent(pickupMode, chargeAmount)
        );

        PlayPickupSound();
        Destroy(gameObject);
    }

    private void PlayPickupSound()
    {
        AudioClip clipToPlay = pickupSound != null
            ? pickupSound
            : Resources.Load<AudioClip>(DefaultPickupSoundResourcePath);

        if (clipToPlay == null)
        {
            Debug.LogWarning($"{nameof(PickupItem)} 没有设置拾取音效，也没有在 Resources/{DefaultPickupSoundResourcePath} 找到默认音效。", this);
            return;
        }

        EnsureAudioListener();

        GameObject audioObject = new GameObject("PickupItem Sound");
        audioObject.transform.position = transform.position;

        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.clip = clipToPlay;
        audioSource.volume = pickupSoundVolume;
        audioSource.spatialBlend = 0f;
        audioSource.Play();

        Destroy(audioObject, clipToPlay.length);
    }

    private void EnsureAudioListener()
    {
        if (FindObjectOfType<AudioListener>() != null)
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.gameObject.AddComponent<AudioListener>();
            return;
        }

        Debug.LogWarning("场景中没有 AudioListener，拾取音效可能无法被听到。", this);
    }
}
