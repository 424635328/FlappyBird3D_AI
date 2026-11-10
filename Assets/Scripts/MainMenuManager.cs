using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; // Keep if needed
using TMPro;
using UnityEngine.Audio;

public class MainMenuManager : MonoBehaviour
{
    [Header("场景名称")]
    public string gameSceneName = "GameScene";

    [Header("UI 引用")]
    // 如果你的下拉菜单是选择游戏模式，最好改名以提高清晰度
    public TMP_Dropdown gameModeDropdown; // <<--- 已改名 (记得在 Inspector 中重新赋值!)
    public Slider volumeSlider;
    public TextMeshProUGUI volumeText;

    [Header("音频设置")]
    public AudioMixer mainMixer;
    public string masterVolumeParam = "MasterVolume";

    // --- 定义游戏模式常量，方便维护 ---
    public const int AImode = 0;
    public const int SimpleMode = 1;
    public const int HardMode = 2;

    void Start()
    {
        // --- 修改：从 SettingsManager 初始化 UI ---
        if (SettingsManager.Instance != null)
        {
            // 初始化下拉菜单
            if (gameModeDropdown != null)
            {
                // 设置下拉菜单的值为 SettingsManager 中存储的值
                gameModeDropdown.value = SettingsManager.Instance.SelectedGameMode;
                // 刷新下拉菜单以显示正确选项
                gameModeDropdown.RefreshShownValue();
            }
            else { Debug.LogWarning("MainMenuManager: gameModeDropdown 未赋值！"); }

            // 初始化音量滑块
            if (volumeSlider != null)
            {
                volumeSlider.value = SettingsManager.Instance.MasterVolume;
                ApplyVolumeSetting(); // 应用加载的音量到 AudioMixer
                UpdateVolumeText();   // 更新音量文本显示
            }
            else { Debug.LogWarning("MainMenuManager: volumeSlider 未赋值！"); }
        }
        else
        {
            Debug.LogError("MainMenuManager 无法找到 SettingsManager 实例！请确保 SettingsManager GameObject 在场景中并且脚本已附加。");
            // 在没有 SettingsManager 时提供默认行为（可选）
            InitializeUIDefaults();
        }
        // --- 修改结束 ---
    }

    // --- 按钮点击事件处理 ---

    public void StartGame()
    {
        // --- 修改：不再需要调用 SaveSettings() ---
        // SettingsManager 应该在设置更改时就已经保存了（如果它内部处理了持久化）
        // 或者 SettingsManager 本身就是跨场景的数据容器
        SceneManager.LoadScene(gameSceneName);
    }

    public void QuitGame()
    {
        Debug.Log("退出游戏...");
        Application.Quit();
    }

    // --- 设置处理 ---

    // 当游戏模式下拉菜单值改变时调用
    public void OnGameModeChanged() // <<--- 方法名建议更改以匹配变量名
    {
        // --- 修改：调用 SettingsManager.Instance.SetGameMode ---
        if (gameModeDropdown != null && SettingsManager.Instance != null)
        {
            int selectedModeIndex = gameModeDropdown.value;
            Debug.Log($"MainMenu: 游戏模式选择 -> {gameModeDropdown.options[selectedModeIndex].text} (Index: {selectedModeIndex})");
            SettingsManager.Instance.SetGameMode(selectedModeIndex); // 通知 SettingsManager 更改
        }
        // --- 修改结束 ---
    }

    // 当音量滑块值改变时调用
    public void OnVolumeChanged()
    {
        // --- 修改：调用 SettingsManager.Instance.SetVolume ---
        ApplyVolumeSetting(); // 1. 应用到 AudioMixer (如果需要实时效果)
        UpdateVolumeText();   // 2. 更新 UI 显示

        // 3. 通知 SettingsManager 更改
        if (volumeSlider != null && SettingsManager.Instance != null)
        {
             SettingsManager.Instance.SetVolume(volumeSlider.value);
        }
        // --- 修改结束 ---
    }

    // --- ApplyVolumeSetting 和 UpdateVolumeText 保持不变 ---
    private void ApplyVolumeSetting()
    {
        if (mainMixer != null && volumeSlider != null)
        {
            float dB = (volumeSlider.value > 0.001f) ? Mathf.Log10(volumeSlider.value) * 20f : -80f;
            mainMixer.SetFloat(masterVolumeParam, dB);
        }
    }

    private void UpdateVolumeText()
    {
        if (volumeText != null && volumeSlider != null)
        {
            volumeText.text = $"音量: {volumeSlider.value * 100:F0}%";
        }
    }

    // --- 新增：用于在 SettingsManager 不存在时的默认初始化（可选） ---
    private void InitializeUIDefaults()
    {
         Debug.LogWarning("SettingsManager 未找到，UI 将使用默认值初始化。");
         if (gameModeDropdown != null) { gameModeDropdown.value = SimpleMode; gameModeDropdown.RefreshShownValue(); } // 默认简单模式
         if (volumeSlider != null) { volumeSlider.value = 1.0f; } // 默认最大音量
         ApplyVolumeSetting();
         UpdateVolumeText();
    }

    // --- 移除 SaveSettings 和 LoadSettings 方法 ---
    // 这两个方法现在不再需要，因为其功能由 SettingsManager 处理
    /*
    private void SaveSettings() { ... } // 删除或注释掉
    private void LoadSettings() { ... } // 删除或注释掉
    */
}