using UnityEngine;
using System.Collections; // 如果需要协程等
using System.Collections.Generic; // 如果需要列表

// 定义播放模式的枚举
public enum MusicPlaybackMode
{
    Stop,                // 停止播放
    SingleTrackLoop,     // 单曲循环播放指定曲目
    PlaylistSequential,  // 播放列表顺序播放并循环
    PlaylistRandom       // 播放列表随机播放
}

[RequireComponent(typeof(AudioSource))]
public class BackgroundMusicManager : MonoBehaviour
{
    [Header("音乐播放列表")]
    [Tooltip("将所有想要播放的背景音乐拖拽到这里")]
    public AudioClip[] musicPlaylist;

    [Header("播放设置")]
    [Tooltip("选择初始的音乐播放模式")]
    public MusicPlaybackMode initialPlaybackMode = MusicPlaybackMode.PlaylistSequential; // 默认顺序播放
    [Tooltip("如果初始模式是单曲循环，指定要循环的曲目索引 (从0开始)")]
    public int initialSingleLoopTrackIndex = 0;
    [Tooltip("是否在游戏开始时自动根据初始模式播放音乐")]
    public bool playOnStart = true;

    // 私有变量
    private AudioSource audioSource;
    private MusicPlaybackMode currentPlaybackMode; // 当前激活的播放模式
    private int currentTrackIndex = -1; // 当前或上一首播放的索引

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false; // 禁用自动播放
        // 在 Awake 中设置当前模式为初始模式，但播放操作放到 Start 中
        currentPlaybackMode = initialPlaybackMode;
        // 确保 AudioSource 的 loop 初始是关闭的，由我们的逻辑控制
        audioSource.loop = false;
    }

    void Start()
    {
        // 检查索引有效性
        if (initialSingleLoopTrackIndex < 0 || initialSingleLoopTrackIndex >= musicPlaylist.Length)
        {
            Debug.LogWarning($"指定的初始单曲循环索引 {initialSingleLoopTrackIndex} 无效，将使用索引 0。请确保播放列表不为空。");
            initialSingleLoopTrackIndex = 0;
        }

        if (playOnStart)
        {
            StartPlaybackBasedOnMode(currentPlaybackMode, initialSingleLoopTrackIndex);
        }
    }

    void Update()
    {
        // 仅在 AudioSource 没有播放、有片段、列表不为空时检查是否需要切换
        if (!audioSource.isPlaying && audioSource.clip != null && musicPlaylist.Length > 0)
        {
            // 根据当前模式决定下一步操作
            switch (currentPlaybackMode)
            {
                case MusicPlaybackMode.PlaylistSequential:
                    // 顺序播放模式下，播放下一首
                    Debug.Log("顺序播放：当前曲目结束，播放下一首...");
                    PlayNextTrackSequential();
                    break;
                case MusicPlaybackMode.PlaylistRandom:
                    // 随机播放模式下，播放下一首随机的
                    Debug.Log("随机播放：当前曲目结束，播放下一首...");
                    PlayNextTrackRandom();
                    break;
                case MusicPlaybackMode.SingleTrackLoop:
                    // 单曲循环模式下，AudioSource 的 loop 属性会处理重复播放
                    // 如果因为某种原因 loop 被关掉了，我们可以在这里重新播放
                    if (!audioSource.loop)
                    {
                         Debug.LogWarning("单曲循环模式下检测到 AudioSource.loop 为 false，尝试重新播放当前曲目...");
                         if(currentTrackIndex >= 0 && currentTrackIndex < musicPlaylist.Length)
                         {
                             PlayAudioClip(musicPlaylist[currentTrackIndex], currentTrackIndex, true); // 重新播放并确保循环开启
                         }
                    }
                    // 正常情况由 AudioSource.loop 处理，这里不需要切换
                    break;
                case MusicPlaybackMode.Stop:
                    // 停止模式下，音乐播完了就停着，不做任何事
                    break;
            }
        }
    }

    // --- 公共方法：用于从其他脚本控制播放模式 ---

    /// <summary>
    /// 切换到指定的播放模式。
    /// </summary>
    /// <param name="newMode">要切换到的新模式。</param>
    /// <param name="trackIndexForSingleLoop">如果新模式是单曲循环，指定要循环的曲目索引。</param>
    public void SetPlaybackMode(MusicPlaybackMode newMode, int trackIndexForSingleLoop = 0)
    {
        Debug.Log($"请求切换播放模式到: {newMode}" + (newMode == MusicPlaybackMode.SingleTrackLoop ? $" (目标索引: {trackIndexForSingleLoop})" : ""));
        StartPlaybackBasedOnMode(newMode, trackIndexForSingleLoop);
    }

    /// <summary>
    /// 切换到单曲循环模式并开始播放指定索引的曲目。
    /// </summary>
    public void PlaySingleLoop(int trackIndex)
    {
        SetPlaybackMode(MusicPlaybackMode.SingleTrackLoop, trackIndex);
    }

    /// <summary>
    /// 切换到播放列表顺序播放模式并开始播放（从下一首开始）。
    /// </summary>
    public void PlaySequential()
    {
        SetPlaybackMode(MusicPlaybackMode.PlaylistSequential);
    }

    /// <summary>
    /// 切换到播放列表随机播放模式并开始播放（随机选一首）。
    /// </summary>
    public void PlayRandom()
    {
        SetPlaybackMode(MusicPlaybackMode.PlaylistRandom);
    }

    /// <summary>
    /// 停止所有音乐播放。
    /// </summary>
    public void Stop()
    {
        SetPlaybackMode(MusicPlaybackMode.Stop);
    }


    // --- 内部辅助方法 ---

    /// <summary>
    /// 根据指定的模式开始或调整播放。
    /// </summary>
    private void StartPlaybackBasedOnMode(MusicPlaybackMode mode, int trackIndex = 0)
    {
        currentPlaybackMode = mode; // 更新当前模式状态

        switch (currentPlaybackMode)
        {
            case MusicPlaybackMode.Stop:
                audioSource.Stop();
                audioSource.clip = null;
                audioSource.loop = false; // 确保停止时 loop 关闭
                Debug.Log("播放模式设置为：停止");
                break;

            case MusicPlaybackMode.SingleTrackLoop:
                // 验证索引
                if (trackIndex < 0 || trackIndex >= musicPlaylist.Length)
                {
                    Debug.LogError($"无法单曲循环：索引 {trackIndex} 无效。播放列表长度为 {musicPlaylist.Length}。");
                    Stop(); // 出错则停止
                    return;
                }
                Debug.Log($"播放模式设置为：单曲循环 (索引: {trackIndex})");
                // 播放指定曲目并开启循环
                PlayAudioClip(musicPlaylist[trackIndex], trackIndex, true);
                break;

            case MusicPlaybackMode.PlaylistSequential:
                Debug.Log("播放模式设置为：顺序播放列表");
                // 从头开始或从下一首开始顺序播放
                currentTrackIndex = -1; // 重置索引，确保从第一首开始
                PlayNextTrackSequential();
                break;

            case MusicPlaybackMode.PlaylistRandom:
                 Debug.Log("播放模式设置为：随机播放列表");
                // 开始随机播放
                PlayNextTrackRandom();
                break;
        }
    }

    /// <summary>
    /// 播放顺序列表中的下一首。
    /// </summary>
    private void PlayNextTrackSequential()
    {
        if (musicPlaylist == null || musicPlaylist.Length == 0) return; // 安全检查
        // 计算下一首索引（循环）
        currentTrackIndex = (currentTrackIndex + 1) % musicPlaylist.Length;
        // 播放该曲目，不开启循环 (因为播放完要切换)
        PlayAudioClip(musicPlaylist[currentTrackIndex], currentTrackIndex, false);
    }

    /// <summary>
    /// 播放随机列表中的下一首。
    /// </summary>
    private void PlayNextTrackRandom()
    {
        if (musicPlaylist == null || musicPlaylist.Length == 0) return; // 安全检查
        int nextIndex;
        // 如果列表多于一首歌，尽量避免立即重复上一首
        if (musicPlaylist.Length > 1)
        {
            do
            {
                nextIndex = Random.Range(0, musicPlaylist.Length);
            } while (nextIndex == currentTrackIndex);
        }
        else
        {
            nextIndex = 0; // 只有一首歌，只能放它
        }
        currentTrackIndex = nextIndex; // 更新当前索引
        // 播放该曲目，不开启循环
        PlayAudioClip(musicPlaylist[currentTrackIndex], currentTrackIndex, false);
    }


    /// <summary>
    /// 内部核心播放方法：设置片段、循环状态并播放。
    /// </summary>
    /// <param name="clip">要播放的音频片段。</param>
    /// <param name="index">该片段的索引（用于记录）。</param>
    /// <param name="loop">是否为此片段开启 AudioSource 的循环。</param>
    private void PlayAudioClip(AudioClip clip, int index, bool loop)
    {
        if (clip != null)
        {
            audioSource.clip = clip;    // 设置音频片段
            audioSource.loop = loop;    // 设置循环状态
            audioSource.Play();         // 开始播放
            currentTrackIndex = index;  // 更新当前播放的索引
            Debug.Log($"开始播放: {clip.name} (索引: {index}), 模式: {currentPlaybackMode}, AudioSource.loop: {audioSource.loop}");
        }
        else
        {
            Debug.LogWarning($"尝试播放索引 {index} 处的音乐，但片段为 null！");
            // 可以选择跳过或尝试播放下一首
            if(currentPlaybackMode == MusicPlaybackMode.PlaylistSequential) PlayNextTrackSequential();
            else if (currentPlaybackMode == MusicPlaybackMode.PlaylistRandom) PlayNextTrackRandom();
        }
    }
}