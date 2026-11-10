// SettingsManager.cs (示例)
using UnityEngine;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    // 使用 GameMode 可能更清晰
    public int SelectedGameMode { get; private set; }
    public float MasterVolume { get; private set; }

    private const string GameModePrefKey = "SelectedGameMode"; // 必须和 MainMenuManager/GameManager 统一
    private const string VolumePrefKey = "MasterVolume";

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettingsFromPrefs(); // 从 PlayerPrefs 加载初始值
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SetGameMode(int modeIndex)
    {
        SelectedGameMode = modeIndex;
        PlayerPrefs.SetInt(GameModePrefKey, SelectedGameMode);
        PlayerPrefs.Save(); // 立即保存
        Debug.Log($"SettingsManager: 游戏模式设置为 {SelectedGameMode}");
    }

    public void SetVolume(float volume)
    {
        MasterVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(VolumePrefKey, MasterVolume);
        PlayerPrefs.Save(); // 立即保存
        Debug.Log($"SettingsManager: 音量设置为 {MasterVolume:F2}");
        // 可能需要立即应用到 AudioMixer，如果 Mixer 是 DontDestroyOnLoad
        // ApplyVolumeToMixer(MasterVolume);
    }

    private void LoadSettingsFromPrefs()
    {
        // 加载模式，默认为 1 (简单模式)
        SelectedGameMode = PlayerPrefs.GetInt(GameModePrefKey, MainMenuManager.SimpleMode); // 使用 MainMenuManager 的常量
        // 加载音量，默认为 1.0
        MasterVolume = PlayerPrefs.GetFloat(VolumePrefKey, 1.0f);
        Debug.Log($"SettingsManager: 从 PlayerPrefs 加载设置 (模式: {SelectedGameMode}, 音量: {MasterVolume:F2})");
    }

    // 如果 AudioMixer 也是 DontDestroyOnLoad 或全局可访问，可以在这里添加应用方法
    // public void ApplyVolumeToMixer(float volume) { ... }
}