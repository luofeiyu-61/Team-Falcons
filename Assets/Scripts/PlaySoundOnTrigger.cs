using UnityEngine;

/// <summary>
/// 玩家进入 2D 触发器时播放音效。
/// 挂到带有 Collider2D（IsTrigger = true）和 AudioSource 的 GameObject 上即可。
/// </summary>
public class PlaySoundOnTrigger : MonoBehaviour
{
    [Header("音效")]
    [Tooltip("要播放的音效剪辑，留空则播放 AudioSource 上预设的 clip")]
    [SerializeField] private AudioClip clip;

    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer;

    [Header("额外选项")]
    [Tooltip("勾选后只触发一次，随后自动销毁此脚本")]
    [SerializeField] private bool playOnce = true;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        if (clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
        else
        {
            audioSource.Play();
        }

        if (playOnce)
        {
            Destroy(this);
        }
    }
}
