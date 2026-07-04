using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoSingleton<AudioManager>
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private AudioClip inGameMusic;
        
    private string previousSceneName = "";
    private static bool _switchingMusic = false;

    protected override void SafeAwake()
    {
        audioSource.volume = 0f;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    public void PlayMenuMusic(float startDuration)
    {
        StartCoroutine(SwitchMusicCoroutine(0f, 1f, startDuration, menuMusic));
    }

    public void PlayInGameMusic(float startDuration)
    {
        StartCoroutine(SwitchMusicCoroutine(0f, 1f, startDuration, inGameMusic));
    }

    public void StopMusic(float stopDuration)
    {
        StartCoroutine(SwitchMusicCoroutine(audioSource.volume, 0f, stopDuration, null));
    }

    public IEnumerator SwitchMusicCoroutine(float from, float to, float duration, AudioClip newClip = null)
    {
        if (_switchingMusic)
            yield break;

        _switchingMusic = true;
        float elapsedTime = 0f;

        if (newClip != null)
        {
            audioSource.clip = newClip;
            audioSource.Play();
        }
        
        while (elapsedTime < duration)
        {
            audioSource.volume = Mathf.Lerp(from, to, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        audioSource.volume = to;
        if (Mathf.Approximately(to, 0f))
        {
            audioSource.Stop();
        }

        _switchingMusic = false;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (audioSource.isPlaying && !_switchingMusic)
        {
            StopMusic(2f);
        }
        
        if (scene.name.Contains("Menu"))
        {
            PlayMenuMusic(2f);
        }
        else if (previousSceneName.Contains("Menu"))
        {
            PlayInGameMusic(2f);
        }
        
        previousSceneName = scene.name;
    }
}